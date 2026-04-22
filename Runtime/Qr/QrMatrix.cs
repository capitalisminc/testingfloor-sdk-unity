using UnityEngine;

namespace TestingFloor.Internal {
    internal sealed class QrMatrix {
        readonly bool[,] _modules;
        public int Version { get; }
        public int Size => _modules.GetLength(0);

        public QrMatrix(bool[,] modules, int version) {
            _modules = modules;
            Version = version;
        }

        public bool this[int row, int col] => _modules[row, col];

        /// Renders the matrix to a `Texture2D` with `scale`-pixel-wide modules and a `quiet`-module quiet zone.
        /// Normal mode: dark = black, light = white. Inverted mode swaps those colors.
        public Texture2D ToTexture(int scale = 1, int quiet = 2, bool inverted = false) {
            if (scale < 1) scale = 1;
            if (quiet < 0) quiet = 0;

            var totalModules = Size + quiet * 2;
            var pixels = totalModules * scale;
            var texture = new Texture2D(pixels, pixels, TextureFormat.RGB24, mipChain: false) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var colors = new Color32[pixels * pixels];
            var white = new Color32(255, 255, 255, 255);
            var black = new Color32(0, 0, 0, 255);
            var light = inverted ? black : white;
            var dark = inverted ? white : black;

            for (var i = 0; i < colors.Length; i++) colors[i] = light;

            for (var r = 0; r < Size; r++) {
                for (var c = 0; c < Size; c++) {
                    if (!_modules[r, c]) continue;
                    var texRow = (r + quiet) * scale;
                    var texCol = (c + quiet) * scale;
                    for (var dy = 0; dy < scale; dy++) {
                        // Flip vertically: texture Y=0 is bottom.
                        var ty = pixels - 1 - (texRow + dy);
                        var baseIdx = ty * pixels;
                        for (var dx = 0; dx < scale; dx++) {
                            colors[baseIdx + texCol + dx] = dark;
                        }
                    }
                }
            }
            texture.SetPixels32(colors);
            texture.Apply(updateMipmaps: false);
            return texture;
        }
    }
}
