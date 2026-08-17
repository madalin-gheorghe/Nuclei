using System;

namespace Nuclei4
{
    /// <summary>
    /// Describes a texture handle without exposing a backend-specific graphics API
    /// to callers. Existing D3D11 shared handles remain the compatibility path;
    /// a future Metal display backend can consume a Metal texture descriptor.
    /// </summary>
    internal readonly struct GpuTextureHandleDescriptor : IEquatable<GpuTextureHandleDescriptor>
    {
        public GpuTextureHandleDescriptor(
            GpuBackendKind backend,
            GpuInteropKind interop,
            IntPtr nativeHandle,
            GpuDeviceIdentity deviceIdentity)
        {
            Backend = backend;
            Interop = interop;
            NativeHandle = nativeHandle;
            DeviceIdentity = deviceIdentity;
        }

        public GpuBackendKind Backend { get; }

        public GpuInteropKind Interop { get; }

        public IntPtr NativeHandle { get; }

        public GpuDeviceIdentity DeviceIdentity { get; }

        public bool IsValid
        {
            get
            {
                return Backend != GpuBackendKind.Unknown
                    && Interop != GpuInteropKind.None
                    && NativeHandle != IntPtr.Zero;
            }
        }

        public static GpuTextureHandleDescriptor Direct3D11SharedTexture(IntPtr sharedHandle)
        {
            return Direct3D11SharedTexture(sharedHandle, default(GpuDeviceIdentity));
        }

        public static GpuTextureHandleDescriptor Direct3D11SharedTexture(
            IntPtr sharedHandle,
            GpuDeviceIdentity deviceIdentity)
        {
            return new GpuTextureHandleDescriptor(
                GpuBackendKind.Direct3D11,
                GpuInteropKind.Direct3D11SharedTexture,
                sharedHandle,
                deviceIdentity);
        }

        public static GpuTextureHandleDescriptor MetalTexture(
            IntPtr textureHandle,
            GpuDeviceIdentity deviceIdentity)
        {
            return new GpuTextureHandleDescriptor(
                GpuBackendKind.Metal,
                GpuInteropKind.MetalTexture,
                textureHandle,
                deviceIdentity);
        }

        public bool Equals(GpuTextureHandleDescriptor other)
        {
            return Backend == other.Backend
                && Interop == other.Interop
                && NativeHandle == other.NativeHandle
                && DeviceIdentity == other.DeviceIdentity;
        }

        public override bool Equals(object obj)
        {
            return obj is GpuTextureHandleDescriptor && Equals((GpuTextureHandleDescriptor)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Backend;
                hash = (hash * 397) ^ (int)Interop;
                hash = (hash * 397) ^ NativeHandle.GetHashCode();
                hash = (hash * 397) ^ DeviceIdentity.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(GpuTextureHandleDescriptor left, GpuTextureHandleDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GpuTextureHandleDescriptor left, GpuTextureHandleDescriptor right)
        {
            return !left.Equals(right);
        }
    }
}
