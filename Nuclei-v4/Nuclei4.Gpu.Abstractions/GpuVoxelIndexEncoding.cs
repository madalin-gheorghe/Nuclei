using System.Runtime.InteropServices;

namespace Nuclei4
{
    // Direction.xyz is a float vector, but Direction.w carries an exact int32
    // voxel address. Numeric float conversion loses addresses above 2^24.
    internal static class GpuVoxelIndexEncoding
    {
        [StructLayout(LayoutKind.Explicit)]
        struct Bits
        {
            [FieldOffset(0)] public int Index;
            [FieldOffset(0)] public float Value;
        }

        public static float Encode(int index) => new Bits { Index = index }.Value;

        public static int Decode(float value) => new Bits { Value = value }.Index;
    }
}
