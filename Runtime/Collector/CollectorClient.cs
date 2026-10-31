using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TestingFloorSdk.Internal {
    internal readonly struct BatchSendOutcome {
        public readonly SendResult Result;
        public readonly int Consumed;
        public readonly int Dropped;
        public readonly int RecordingDropped;

        public BatchSendOutcome(SendResult result, int consumed, int dropped = 0, int recordingDropped = 0) {
            Result = result;
            Consumed = consumed;
            Dropped = dropped;
            RecordingDropped = recordingDropped;
        }
    }

    internal interface IBatchSender {
        ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events);
    }

    internal interface IBlockingBatchSender : IBatchSender {
        BatchSendOutcome CompletePendingBlocking(DateTime deadline);
        BatchSendOutcome TrackBatchBlocking(IReadOnlyList<TelemetryEvent> events, DateTime deadline);
    }

    internal sealed class CollectorClient : IBlockingBatchSender {
        readonly TestingFloorSettings _settings;
        readonly bool _automation;
        readonly JsonPayloadWriter _writer = new();
        bool _invalidWriteKey;
        bool _loggedInvalidWriteKey;
        PendingSend _pending;

        sealed class PendingSend {
            internal UnityWebRequest Request;
            internal Task Task;
            internal int Consumed;
            internal int Written;
            internal int RecordingConsumed;
            internal int RecordingSkipped;
            internal bool Finished;
            internal BatchSendOutcome Outcome;
        }

        public CollectorClient(TestingFloorSettings settings, bool? automation = null) {
            _settings = settings;
            _automation = automation ?? TestingFloorSettings.IsAutomation;
        }

        PendingSend Begin(IReadOnlyList<TelemetryEvent> events, out BatchSendOutcome outcome) {
            outcome = new BatchSendOutcome(SendResult.Success, 0);
            if (events == null || events.Count == 0) return null;
            if (_invalidWriteKey) {
                outcome = new BatchSendOutcome(SendResult.Rejected, events.Count, events.Count, CountRecording(events, events.Count));
                return null;
            }
            if (_settings == null || !_settings.CanDeliver(_automation)) {
                outcome = new BatchSendOutcome(SendResult.NotConfigured, events.Count, events.Count, CountRecording(events, events.Count));
                return null;
            }
            var recordingSkipped = 0;
            var bytes = _writer.BuildBatch(events, _settings.writeKey, TestingFloor.PlaySessionId,
                JsonPayloadWriter.CollectorBodyByteCap - JsonPayloadWriter.BodySafetyMargin,
                JsonPayloadWriter.CollectorEventByteCap, _settings, out var consumed, out var written,
                ev => { if (IsRecordingGameplay(ev)) recordingSkipped++; });
            if (written == 0) {
                outcome = new BatchSendOutcome(SendResult.Success, consumed, consumed, recordingSkipped);
                return null;
            }
            var request = new UnityWebRequest($"{_settings.endpoint.Trim().TrimEnd('/')}/v1/batch", UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 10;
            request.SetRequestHeader("Content-Type", "application/json");
            _pending = new PendingSend { Request = request, Consumed = consumed, Written = written,
                RecordingConsumed = CountRecording(events, consumed), RecordingSkipped = recordingSkipped };
            try { _pending.Task = WebRequestAwaiter.SendAsync(request); }
            catch (Exception ex) {
                if (_settings.logErrors) Debug.LogWarning($"[TestingFloor] Collector request failed: {ex.Message}");
                _pending.Finished = true;
                _pending.Outcome = new BatchSendOutcome(SendResult.TransientFailure, 0);
                request.Dispose();
            }
            return _pending;
        }

        public async ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events) {
            var send = Begin(events, out var outcome);
            if (send == null) return outcome;
            if (!send.Finished) {
                try { await send.Task; }
                catch { return Finish(send, timedOut: true); }
            }
            return Finish(send);
        }

        public BatchSendOutcome CompletePendingBlocking(DateTime deadline) {
            return _pending == null ? new BatchSendOutcome(SendResult.TransientFailure, 0) : Wait(_pending, deadline);
        }

        public BatchSendOutcome TrackBatchBlocking(IReadOnlyList<TelemetryEvent> events, DateTime deadline) {
            var send = Begin(events, out var outcome);
            return send == null ? outcome : Wait(send, deadline);
        }

        BatchSendOutcome Wait(PendingSend send, DateTime deadline) {
            // Unity's HTTP worker progresses while Play-mode teardown blocks the
            // main thread. Read isDone directly; completed callbacks need a frame.
            while (!send.Finished && !send.Request.isDone && DateTime.UtcNow < deadline)
                System.Threading.Thread.Sleep(5);
            return Finish(send, !send.Finished && !send.Request.isDone);
        }

        BatchSendOutcome Finish(PendingSend send, bool timedOut = false) {
            if (send.Finished) return send.Outcome;
            if (timedOut) {
                send.Request.Abort();
                send.Outcome = new BatchSendOutcome(SendResult.TransientFailure, 0);
            } else {
                var result = ClassifyResponse(send.Request.responseCode, send.Request.result == UnityWebRequest.Result.Success);
                if (result == SendResult.Rejected) {
                    _invalidWriteKey = true;
                    if (!_loggedInvalidWriteKey) {
                        _loggedInvalidWriteKey = true;
                        Debug.LogWarning("[TestingFloor] Write key rejected. Collector sends are disabled for this play session.");
                    }
                }
                if (result == SendResult.Success && _settings.logEventSends)
                    Debug.Log($"[TestingFloor] Sent batch of {send.Written} event(s) ({send.Request.responseCode}).");
                else if (result != SendResult.Success && _settings.logErrors)
                    Debug.LogWarning($"[TestingFloor] Collector request failed (HTTP {send.Request.responseCode}).");
                send.Outcome = result == SendResult.TransientFailure
                    ? new BatchSendOutcome(result, 0)
                    : new BatchSendOutcome(result, send.Consumed, result == SendResult.Success ? send.Consumed - send.Written : send.Consumed,
                        result == SendResult.Success ? send.RecordingSkipped : send.RecordingConsumed);
            }
            send.Finished = true;
            send.Request.Dispose();
            return send.Outcome;
        }

        internal static bool IsRecordingGameplay(TelemetryEvent ev) => ev.Recording != null && ev.EventType != "$tf_telemetry_complete";

        internal static int CountRecording(IReadOnlyList<TelemetryEvent> events, int count) {
            var result = 0;
            for (var i = 0; i < count; i++) if (IsRecordingGameplay(events[i])) result++;
            return result;
        }

        internal static SendResult ClassifyResponse(long status, bool succeeded = true) {
            if (status >= 200 && status < 300) return succeeded ? SendResult.Success : SendResult.TransientFailure;
            if (status == 401 || status == 403) return SendResult.Rejected;
            if (status >= 400 && status < 500 && status != 408 && status != 429) return SendResult.InvalidRequest;
            return SendResult.TransientFailure;
        }
    }
}
