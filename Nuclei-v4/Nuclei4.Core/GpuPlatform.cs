using System;

namespace Nuclei4
{
    /// <summary>
    /// Identifies the native API that owns a GPU resource or simulation session.
    /// Metal is reserved for the future macOS backend; no Metal implementation is
    /// included in the current Windows release.
    /// </summary>
    internal enum GpuBackendKind
    {
        Unknown = 0,
        Direct3D11 = 1,
        Metal = 2
    }

    /// <summary>
    /// Describes how a native resource can be consumed by a display backend.
    /// The abstraction deliberately does not assume every native handle is DXGI.
    /// </summary>
    internal enum GpuInteropKind
    {
        None = 0,
        Direct3D11SharedTexture = 1,
        MetalTexture = 2,
        CpuMemory = 3
    }

    /// <summary>
    /// Backend-defined physical-device identity. Direct3D can store an adapter
    /// LUID and Metal can later store a registry identifier without changing the
    /// display contracts.
    /// </summary>
    internal readonly struct GpuDeviceIdentity : IEquatable<GpuDeviceIdentity>
    {
        public GpuDeviceIdentity(GpuBackendKind backend, ulong low, ulong high = 0)
        {
            Backend = backend;
            Low = low;
            High = high;
        }

        public GpuBackendKind Backend { get; }

        public ulong Low { get; }

        public ulong High { get; }

        public bool IsKnown
        {
            get { return Backend != GpuBackendKind.Unknown && (Low != 0 || High != 0); }
        }

        public bool Equals(GpuDeviceIdentity other)
        {
            return Backend == other.Backend && Low == other.Low && High == other.High;
        }

        public override bool Equals(object obj)
        {
            return obj is GpuDeviceIdentity && Equals((GpuDeviceIdentity)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Backend;
                hash = (hash * 397) ^ Low.GetHashCode();
                hash = (hash * 397) ^ High.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(GpuDeviceIdentity left, GpuDeviceIdentity right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GpuDeviceIdentity left, GpuDeviceIdentity right)
        {
            return !left.Equals(right);
        }
    }
}
