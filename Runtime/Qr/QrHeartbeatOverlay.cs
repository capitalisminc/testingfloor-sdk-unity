using System;
using TestingFloor.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace TestingFloor {
    /// Draws a telemetry sync QR code in a fixed screen corner.
    /// Payload is `tfqr://sync/v1?s=<session_id>&t=<unix_ms>&q=<sequence>`.
    internal sealed class QrHeartbeatOverlay : MonoBehaviour {
        const int BaselineQrSize = 120;
        const int MinQrSize = 96;
        const int QuietZoneModules = 4;

        TestingFloorSettings _settings;
        Canvas _canvas;
        RawImage _image;
        Texture2D _texture;
        float _nextRefreshAt;
        float _visibleUntil;
        long _sequence;
        bool _hasPayload;
        bool _lastInverted;

        void Awake() {
            _settings = TestingFloorSettings.Current;
            gameObject.hideFlags = HideFlags.HideAndDontSave;

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = int.MaxValue;

            var imageGo = new GameObject("QR");
            imageGo.transform.SetParent(transform, false);
            _image = imageGo.AddComponent<RawImage>();
            _image.raycastTarget = false;
            _image.color = Color.white;

            var rect = _image.rectTransform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-16f, -16f);

            _image.enabled = false;
        }

        void Update() {
            if (_settings == null) return;
            if (!QrHeartbeatDriver.EffectiveEnabled) {
                if (_image.enabled) _image.enabled = false;
                return;
            }

            var now = Time.realtimeSinceStartup;
            var inverted = QrHeartbeatDriver.EffectiveInverted;
            var interval = Mathf.Max(1f, _settings.qrHeartbeatIntervalSeconds);
            if (!_hasPayload || inverted != _lastInverted || now >= _nextRefreshAt) {
                if (Fire(inverted)) {
                    _nextRefreshAt = now + interval;
                    var visibleSeconds = Mathf.Max(0f, _settings.qrHeartbeatVisibleSeconds);
                    _visibleUntil = visibleSeconds <= 0f
                        ? float.PositiveInfinity
                        : now + Mathf.Min(visibleSeconds, interval);
                }
                else {
                    _nextRefreshAt = now + 1f;
                }
            }

            _image.enabled = _hasPayload && now < _visibleUntil;
        }

        bool Fire(bool inverted) {
            var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sequence = _sequence + 1;
            var payload = QrHeartbeatPayload.Build(TestingFloorSession.EffectiveSessionId, unixMs, sequence);
            try {
                var matrix = QrEncoder.Encode(payload);
                if (_texture != null) Destroy(_texture);

                var targetSizePx = Mathf.Max(MinQrSize, Mathf.FloorToInt(BaselineQrSize * (Screen.height / 1080f)));
                var totalModules = matrix.Size + QuietZoneModules * 2;
                var moduleScale = Mathf.Max(1, Mathf.CeilToInt(targetSizePx / (float)totalModules));
                _texture = matrix.ToTexture(scale: moduleScale, quiet: QuietZoneModules, inverted: inverted);
                _image.texture = _texture;
                _image.rectTransform.sizeDelta = new Vector2(_texture.width, _texture.height);
                _sequence = sequence;
                _lastInverted = inverted;
                _hasPayload = true;
                return true;
            }
            catch (Exception e) {
                if (_settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] QR heartbeat encode failed: {e.Message}");
                }
                _image.enabled = false;
                _hasPayload = false;
                return false;
            }
        }

        void OnDestroy() {
            if (_texture != null) Destroy(_texture);
        }
    }
}
