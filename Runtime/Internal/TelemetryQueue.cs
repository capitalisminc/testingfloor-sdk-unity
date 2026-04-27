using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace TestingFloor.Internal {
    internal static class TelemetryQueue {
        const int MaxPendingEvents = 256;
        const int InitialQueueCapacity = 32;
        const int MaxPooledDictionaries = 64;
        const int PooledDictionaryCapacity = 16;
        const int DefaultBatchMaxEvents = 50;
        const float DefaultBatchFlushIntervalSeconds = 0.25f;
        // How long to wait between polls when filling a batch's flush window.
        // Short enough that the window is honored within ~10ms, long enough that
        // an idle sender doesn't burn CPU.
        const int BatchFillPollMilliseconds = 10;

        static readonly object s_queueLock = new();
        static Queue<TelemetryEvent> _pendingEvents;
        static Stack<Dictionary<string, object>> _dictionaryPool;
        static bool _senderRunning;
        static bool _isSending;
        static int _droppedEventCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            _pendingEvents = new Queue<TelemetryEvent>(InitialQueueCapacity);
            _dictionaryPool = new Stack<Dictionary<string, object>>(MaxPooledDictionaries);
            _senderRunning = false;
            _isSending = false;
            _droppedEventCount = 0;
        }

        public static Dictionary<string, object> RentProperties() {
            lock (s_queueLock) {
                _dictionaryPool ??= new Stack<Dictionary<string, object>>(MaxPooledDictionaries);
                return _dictionaryPool.Count > 0
                    ? _dictionaryPool.Pop()
                    : new Dictionary<string, object>(PooledDictionaryCapacity);
            }
        }

        public static void ReturnProperties(Dictionary<string, object> properties) {
            if (properties == null) return;
            properties.Clear();
            lock (s_queueLock) {
                _dictionaryPool ??= new Stack<Dictionary<string, object>>(MaxPooledDictionaries);
                if (_dictionaryPool.Count < MaxPooledDictionaries) {
                    _dictionaryPool.Push(properties);
                }
            }
        }

        public static void Enqueue(TelemetryEvent ev, TestingFloorSettings settings, Func<CollectorClient> clientFactory, Action<SendResult> onResult) {
            lock (s_queueLock) {
                _pendingEvents ??= new Queue<TelemetryEvent>(InitialQueueCapacity);
                if (_pendingEvents.Count >= MaxPendingEvents) {
                    var dropped = _pendingEvents.Dequeue();
                    ReturnPropertiesUnlocked(dropped.EventProperties);
                    ReturnPropertiesUnlocked(dropped.Context.Properties);
                    _droppedEventCount++;
                    if (settings != null && settings.logErrors && _droppedEventCount % 25 == 1) {
                        Debug.LogWarning($"[TestingFloor] Dropping events; queue full ({MaxPendingEvents}). Dropped={_droppedEventCount}.");
                    }
                }
                _pendingEvents.Enqueue(ev);
            }
            EnsureSenderRunning(clientFactory, onResult, settings);
        }

        public static async Task FlushAsync(TimeSpan timeout, Func<CollectorClient> clientFactory, Action<SendResult> onResult, TestingFloorSettings settings) {
            if (timeout <= TimeSpan.Zero) return;
            EnsureSenderRunning(clientFactory, onResult, settings);

            var drain = WaitUntilIdle();
            var delay = Task.Delay(timeout);
            await Task.WhenAny(drain, delay);
        }

        public static bool HasPending() {
            lock (s_queueLock) {
                return _pendingEvents != null && _pendingEvents.Count > 0;
            }
        }

        static void EnsureSenderRunning(Func<CollectorClient> clientFactory, Action<SendResult> onResult, TestingFloorSettings settings) {
            bool shouldStart;
            lock (s_queueLock) {
                if (_senderRunning) return;
                _senderRunning = true;
                shouldStart = true;
            }
            if (shouldStart) {
                _ = RunSenderLoop(clientFactory, onResult, settings);
            }
        }

        static async Task RunSenderLoop(Func<CollectorClient> clientFactory, Action<SendResult> onResult, TestingFloorSettings settings) {
            // Allocate the working batch list once for the lifetime of this sender.
            // Reused for every batch — capacity grows to fit the largest one we see.
            var batch = new List<TelemetryEvent>(GetMaxBatchEvents(settings));

            try {
                while (true) {
                    // Phase 1: exit if the queue is empty. New events flip _senderRunning back on.
                    lock (s_queueLock) {
                        if (_pendingEvents == null || _pendingEvents.Count == 0) {
                            _senderRunning = false;
                            _isSending = false;
                            return;
                        }
                    }

                    // Phase 2: fill the batch. Drain whatever's already queued, then optionally
                    // wait up to flushIntervalSeconds for more events to bunch up.
                    batch.Clear();
                    var maxBatchEvents = GetMaxBatchEvents(settings);
                    var flushIntervalSeconds = GetBatchFlushIntervalSeconds(settings);
                    var deadline = DateTime.UtcNow.AddSeconds(flushIntervalSeconds);

                    while (batch.Count < maxBatchEvents) {
                        lock (s_queueLock) {
                            while (batch.Count < maxBatchEvents && _pendingEvents.Count > 0) {
                                batch.Add(_pendingEvents.Dequeue());
                            }
                            if (batch.Count > 0) {
                                _isSending = true;
                            }
                        }
                        if (batch.Count >= maxBatchEvents) break;
                        if (flushIntervalSeconds <= 0f) break;
                        if (DateTime.UtcNow >= deadline) break;
                        await Task.Delay(BatchFillPollMilliseconds);
                    }

                    if (batch.Count == 0) continue; // nothing to send; loop back to Phase 1

                    // Phase 3: send. CollectorClient.TrackBatchAsync internally enforces the
                    // collector's per-event and body byte caps; if it can't fit everything in
                    // one request, it reports back how many events were "consumed" (i.e., either
                    // sent or dropped as oversized). Anything beyond that we have to put back at
                    // the front of the queue so it's tried in the next iteration.
                    var client = clientFactory();
                    var outcome = await SendSafelyAsync(client, batch, settings);

                    var consumed = Math.Min(outcome.Consumed, batch.Count);
                    if (consumed < 0) consumed = 0;

                    onResult?.Invoke(outcome.Result);

                    // Phase 4a: return pooled dicts for the events that left the queue for good.
                    for (var i = 0; i < consumed; i++) {
                        ReturnProperties(batch[i].EventProperties);
                        ReturnProperties(batch[i].Context.Properties);
                    }

                    // Phase 4b: any unconsumed events go back at the head of the queue, in order,
                    // so the next iteration tries them first.
                    if (consumed < batch.Count) {
                        RequeueAtFront(batch, consumed);
                    }

                    lock (s_queueLock) {
                        _isSending = false;
                    }
                }
            }
            catch (Exception ex) {
                lock (s_queueLock) {
                    _senderRunning = false;
                    _isSending = false;
                }
                if (settings != null && settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] Sender loop failed: {ex}");
                }
            }
        }

        static async Task<BatchSendOutcome> SendSafelyAsync(CollectorClient client, IReadOnlyList<TelemetryEvent> batch, TestingFloorSettings settings) {
            try {
                return await client.TrackBatchAsync(batch);
            }
            catch (Exception ex) {
                if (settings != null && settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] Send failed: {ex}");
                }
                // Treat unhandled exceptions as if the whole batch was consumed so we don't
                // wedge the queue retrying the same poison batch forever. Same drop semantics
                // as the pre-batch implementation.
                return new BatchSendOutcome(SendResult.TransientFailure, batch?.Count ?? 0);
            }
        }

        static void RequeueAtFront(List<TelemetryEvent> batch, int consumed) {
            var leftover = batch.Count - consumed;
            if (leftover <= 0) return;
            lock (s_queueLock) {
                _pendingEvents ??= new Queue<TelemetryEvent>(InitialQueueCapacity);
                // Re-build the queue with the leftovers up front, followed by anything that
                // arrived while we were sending. There's no Queue.Prepend, so swap into a
                // fresh queue. Allocations here are bounded by batchMaxEvents + queue size.
                var rebuilt = new Queue<TelemetryEvent>(leftover + _pendingEvents.Count);
                for (var i = consumed; i < batch.Count; i++) {
                    rebuilt.Enqueue(batch[i]);
                }
                while (_pendingEvents.Count > 0) {
                    rebuilt.Enqueue(_pendingEvents.Dequeue());
                }
                _pendingEvents = rebuilt;
            }
        }

        static int GetMaxBatchEvents(TestingFloorSettings settings) {
            if (settings == null) return DefaultBatchMaxEvents;
            return Math.Max(1, settings.batchMaxEvents);
        }

        static float GetBatchFlushIntervalSeconds(TestingFloorSettings settings) {
            if (settings == null) return DefaultBatchFlushIntervalSeconds;
            return Math.Max(0f, settings.batchMaxFlushIntervalSeconds);
        }

        static async Task WaitUntilIdle() {
            while (true) {
                lock (s_queueLock) {
                    if ((_pendingEvents == null || _pendingEvents.Count == 0) && !_isSending) return;
                }
                await Task.Yield();
            }
        }

        static void ReturnPropertiesUnlocked(Dictionary<string, object> properties) {
            if (properties == null) return;
            properties.Clear();
            _dictionaryPool ??= new Stack<Dictionary<string, object>>(MaxPooledDictionaries);
            if (_dictionaryPool.Count < MaxPooledDictionaries) {
                _dictionaryPool.Push(properties);
            }
        }
    }
}
