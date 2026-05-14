// SPDX-License-Identifier: MIT

using UnityEngine;

namespace GaussianSplatting.Runtime
{
    /// <summary>
    /// Holds fully-processed Gaussian Splat data in memory ready to upload to the GPU.
    /// Created by <see cref="RuntimeSplatProcessing"/> from a downloaded SPZ file.
    /// Does NOT require Unity's AssetDatabase — safe to create and use in builds.
    /// </summary>
    public class RuntimeSplatData
    {
        // ── Metadata ─────────────────────────────────────────────────────────
        public int splatCount;
        public Vector3 boundsMin;
        public Vector3 boundsMax;

        // ── Format descriptors (match GaussianSplatAsset enums) ──────────────
        public GaussianSplatAsset.VectorFormat posFormat;
        public GaussianSplatAsset.VectorFormat scaleFormat;
        public GaussianSplatAsset.ColorFormat  colorFormat;
        public GaussianSplatAsset.SHFormat     shFormat;

        // ── GPU-ready byte buffers ────────────────────────────────────────────
        /// <summary>Position data, encoded in <see cref="posFormat"/>.</summary>
        public byte[] posData;

        /// <summary>Rotation (Norm10) + Scale (encoded in <see cref="scaleFormat"/>) per splat.
        /// May also contain a 2-byte SH cluster index at the end of each entry.</summary>
        public byte[] othData;

        /// <summary>Color data, always stored as Float32×4 (r,g,b,opacity) per splat.
        /// The renderer converts this to the target <see cref="colorFormat"/> texture at load time.</summary>
        public byte[] colData;

        /// <summary>Spherical Harmonics data, encoded in <see cref="shFormat"/>.
        /// For cluster formats this is the cluster table; for per-splat formats each splat has its own entry.</summary>
        public byte[] shData;

        /// <summary>Chunk bounds data for lossy formats. Null when all formats are Float32 (no chunking needed).</summary>
        public byte[] chkData;

        // ── Optional world metadata ───────────────────────────────────────────
        public string worldId;
        public string worldName;
        public string thumbnailUrl;
    }
}
