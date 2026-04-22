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
            try {
                while (true) {
                    TelemetryEvent nextEvent;
                    lock (s_queueLock) {
                        if (_pendingEvents == null || _pendingEvents.Count == 0) {
                            _senderRunning = false;
                            _isSending = false;
                            return;
                        }
                        nextEvent = _pendingEvents.Dequeue();
                        _isSending = true;
                    }

                    var client = clientFactory();
                    var result = await SendSafelyAsync(client, nextEvent, settings);
                    onResult?.Invoke(result);
                    ReturnProperties(nextEvent.EventProperties);
                    ReturnProperties(nextEvent.Context.Properties);

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

        static async Task<SendResult> SendSafelyAsync(CollectorClient client, TelemetryEvent ev, TestingFloorSettings settings) {
            try {
                return await client.TrackEventAsync(ev);
            }
            catch (Exception ex) {
                if (settings != null && settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] Send failed: {ex}");
                }
                return SendResult.TransientFailure;
            }
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
