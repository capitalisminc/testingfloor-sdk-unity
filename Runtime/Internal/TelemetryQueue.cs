using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TestingFloorSdk.Internal {
    internal static class TelemetryQueue {
        const int MaxPooledDictionaries = 64;
        static readonly object QueueLock = new();
        static Queue<TelemetryEvent> _pending = new();
        static Stack<Dictionary<string, object>> _pool = new();
        static CancellationTokenSource _lifetime = new();
        static bool _senderRunning;
        static bool _flushRequested;
        static int _inFlight;
        static List<TelemetryEvent> _activeBatch;
        static bool _requestInFlight;
        static int _dropped;
        static int _recordingDropped;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() {
            lock (QueueLock) {
                _lifetime.Cancel();
                _lifetime.Dispose();
                _lifetime = new CancellationTokenSource();
                _pending = new Queue<TelemetryEvent>();
                _pool = new Stack<Dictionary<string, object>>();
                _senderRunning = false;
                _flushRequested = false;
                _inFlight = 0;
                _activeBatch = null;
                _requestInFlight = false;
                _dropped = 0;
                _recordingDropped = 0;
            }
        }

        internal static int PendingEventCount { get { lock (QueueLock) return _pending.Count + _inFlight; } }
        internal static int DroppedEventCount { get { lock (QueueLock) return _dropped; } }
        public static bool HasPending() => PendingEventCount > 0;

        public static Dictionary<string, object> RentProperties() {
            lock (QueueLock) return _pool.Count > 0 ? _pool.Pop() : new Dictionary<string, object>(16);
        }

        public static void ReturnProperties(Dictionary<string, object> properties) {
            if (properties == null) return;
            lock (QueueLock) {
                properties.Clear();
                if (_pool.Count < MaxPooledDictionaries) _pool.Push(properties);
            }
        }

        static void Release(TelemetryEvent ev) {
            ReturnProperties(ev.EventProperties);
            ReturnProperties(ev.Context.Properties);
        }

        public static bool Enqueue(TelemetryEvent ev, TestingFloorSettings settings, Func<IBatchSender> clientFactory, Action<SendResult> onResult) {
            lock (QueueLock) {
                if (ev.EventType != "$tf_telemetry_complete" && _pending.Count + _inFlight >= Math.Max(1, settings != null ? settings.queueMaxEvents : 4096)) {
                    Release(ev);
                    _dropped++;
                    if (CollectorClient.IsRecordingGameplay(ev)) _recordingDropped++;
                    return false;
                }
                _pending.Enqueue(ev);
                if (ev.EventType == "$tf_telemetry_complete") _flushRequested = true;
            }
            EnsureSenderRunning(clientFactory, onResult, settings);
            return true;
        }

        public static async Task FlushAsync(TimeSpan timeout, Func<IBatchSender> clientFactory, Action<SendResult> onResult, TestingFloorSettings settings) {
            if (timeout <= TimeSpan.Zero) return;
            var limit = Math.Min(5.0, timeout.TotalSeconds);
            _flushRequested = true;
            EnsureSenderRunning(clientFactory, onResult, settings);
            var clock = Stopwatch.StartNew();
            while (HasPending() && clock.Elapsed.TotalSeconds < limit) await Task.Delay(10);
        }

        static void ApplyOutcome(List<TelemetryEvent> batch, BatchSendOutcome outcome) {
            lock (QueueLock) {
                var consumed = Math.Clamp(outcome.Consumed, 0, batch.Count);
                _recordingDropped += Math.Clamp(outcome.RecordingDropped, 0, consumed);
                for (var i = 0; i < consumed; i++) Release(batch[i]);
                _dropped += Math.Clamp(outcome.Dropped, 0, consumed);
                var retained = new Queue<TelemetryEvent>();
                for (var i = consumed; i < batch.Count; i++) retained.Enqueue(batch[i]);
                while (_pending.Count > 0) retained.Enqueue(_pending.Dequeue());
                _pending = retained;
                _inFlight = 0;
                _activeBatch = null;
                _requestInFlight = false;
                _flushRequested = _pending.Count > 0;
                if (outcome.Result == SendResult.Rejected || outcome.Result == SendResult.NotConfigured) {
                    _dropped += _pending.Count;
                    while (_pending.Count > 0) {
                        var ev = _pending.Dequeue();
                        if (CollectorClient.IsRecordingGameplay(ev)) _recordingDropped++;
                        Release(ev);
                    }
                }
            }
        }

        internal static void FlushBlocking(TimeSpan timeout, IBlockingBatchSender sender, TestingFloorSettings settings) {
            if (sender == null || timeout <= TimeSpan.Zero) return;
            var deadline = DateTime.UtcNow.AddSeconds(Math.Min(5, timeout.TotalSeconds));
            _lifetime.Cancel();
            _lifetime.Dispose();
            _lifetime = new CancellationTokenSource();
            if (_inFlight > 0 && _activeBatch != null) {
                var wasSending = _requestInFlight;
                var outcome = wasSending ? sender.CompletePendingBlocking(deadline) : new BatchSendOutcome(SendResult.TransientFailure, 0);
                ApplyOutcome(_activeBatch, outcome);
                if (wasSending && outcome.Result != SendResult.Success) { _senderRunning = false; return; }
            }
            while (_pending.Count > 0 && DateTime.UtcNow < deadline) {
                var batch = new List<TelemetryEvent>();
                var maximum = Math.Clamp(settings != null ? settings.batchMaxEvents : 50, 1, 2000);
                while (batch.Count < maximum && _pending.Count > 0) {
                    if (batch.Count > 0 && _pending.Peek().EventType == "$tf_telemetry_complete") break;
                    var next = _pending.Dequeue();
                    if (next.EventType == "$tf_telemetry_complete") next.EventProperties["dropped_events"] = _recordingDropped;
                    batch.Add(next);
                    if (next.EventType == "$tf_telemetry_complete") break;
                }
                var outcome = sender.TrackBatchBlocking(batch, deadline);
                ApplyOutcome(batch, outcome);
                if (outcome.Result != SendResult.Success) break;
            }
            _senderRunning = false;
        }

        static void EnsureSenderRunning(Func<IBatchSender> clientFactory, Action<SendResult> onResult, TestingFloorSettings settings) {
            lock (QueueLock) {
                if (_senderRunning || _pending.Count == 0) return;
                _senderRunning = true;
            }
            _ = RunSenderLoop(clientFactory, onResult, settings, _lifetime.Token);
        }

        static async Task RunSenderLoop(Func<IBatchSender> clientFactory, Action<SendResult> onResult, TestingFloorSettings settings, CancellationToken lifetime) {
            var batch = new List<TelemetryEvent>();
            var retrySeconds = 1;
            try {
                while (!lifetime.IsCancellationRequested) {
                    batch.Clear();
                    var maximum = Math.Clamp(settings != null ? settings.batchMaxEvents : 50, 1, 2000);
                    var interval = settings != null ? settings.batchMaxFlushIntervalSeconds : 0.25f;
                    if (float.IsNaN(interval) || float.IsInfinity(interval)) interval = 0.25f;
                    var clock = Stopwatch.StartNew();
                    while (true) {
                        lock (QueueLock) {
                            while (batch.Count < maximum && _pending.Count > 0) {
                                if (batch.Count > 0 && _pending.Peek().EventType == "$tf_telemetry_complete") break;
                                var next = _pending.Dequeue();
                                if (next.EventType == "$tf_telemetry_complete") next.EventProperties["dropped_events"] = _recordingDropped;
                                batch.Add(next);
                                if (next.EventType == "$tf_telemetry_complete") break;
                            }
                            _activeBatch = batch;
                            _inFlight = batch.Count;
                        }
                        if ((batch.Count > 0 && batch[0].EventType == "$tf_telemetry_complete") || batch.Count >= maximum || _flushRequested || clock.Elapsed.TotalSeconds >= Math.Max(0, interval)) break;
                        await Task.Delay(10, lifetime);
                    }
                    if (batch.Count == 0) return;
                    var client = clientFactory();
                    BatchSendOutcome outcome;
                    _requestInFlight = client != null;
                    try {
                        outcome = client == null
                            ? new BatchSendOutcome(SendResult.NotConfigured, batch.Count, batch.Count, CollectorClient.CountRecording(batch, batch.Count))
                            : await client.TrackBatchAsync(batch);
                    }
                    catch (Exception) {
                        outcome = new BatchSendOutcome(SendResult.TransientFailure, 0);
                    }
                    lifetime.ThrowIfCancellationRequested();
                    onResult?.Invoke(outcome.Result);
                    ApplyOutcome(batch, outcome);
                    if (!HasPending()) return;
                    if (outcome.Result == SendResult.TransientFailure) {
                        await Task.Delay(TimeSpan.FromSeconds(retrySeconds), lifetime);
                        retrySeconds = Math.Min(30, retrySeconds * 2);
                    }
                    else retrySeconds = 1;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) {
                if (settings != null && settings.logErrors) Debug.LogWarning("[TestingFloor] Sender stopped unexpectedly.");
            }
            finally {
                var restart = false;
                lock (QueueLock) {
                    if (!lifetime.IsCancellationRequested) {
                        _senderRunning = false;
                        _flushRequested = false;
                        restart = _pending.Count > 0;
                    }
                }
                if (restart) EnsureSenderRunning(clientFactory, onResult, settings);
            }
        }
    }
}
