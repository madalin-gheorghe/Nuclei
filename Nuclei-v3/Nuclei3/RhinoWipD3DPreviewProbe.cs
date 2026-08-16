using Rhino;
using Rhino.Display;

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Nuclei3
{
    internal static class RhinoWipD3DPreviewProbe
    {
        const string OutputPath = @"C:\Nuclei\Nuclei-v3\BenchmarkSuite1\NucleiD3DPreviewProbe.txt";

        static int hasWrittenProbe = 0;

        public static bool TryGetRhinoD3D(DisplayPipeline display, RhinoViewport viewport, out IntPtr devicePtr, out IntPtr contextPtr)
        {
            devicePtr = IntPtr.Zero;
            contextPtr = IntPtr.Zero;

            if (SafeRhinoVersion() < 9) return false;
            if (!RhUsingDirect3D()) return false;

            IntPtr pipelinePtr = TryGetDisplayPipelinePointer(display, viewport);
            if (pipelinePtr == IntPtr.Zero) return false;

            devicePtr = GetDirect3d11Device(pipelinePtr);
            contextPtr = GetDirect3d11DeviceContext(pipelinePtr);

            return devicePtr != IntPtr.Zero && contextPtr != IntPtr.Zero;
        }

        public static void TryWriteProbe(DisplayPipeline display, RhinoViewport viewport)
        {
            if (Interlocked.Exchange(ref hasWrittenProbe, 1) != 0) return;

            string status;
            try
            {
                int rhinoVersion = SafeRhinoVersion();
                if (rhinoVersion < 9)
                {
                    status = "Rhino version is below 9; WIP D3D preview probe skipped.";
                }
                else
                {
                    bool usingDirect3D = RhUsingDirect3D();
                    IntPtr pipelinePtr = TryGetDisplayPipelinePointer(display, viewport);

                    IntPtr devicePtr = IntPtr.Zero;
                    IntPtr contextPtr = IntPtr.Zero;

                    if (pipelinePtr != IntPtr.Zero && usingDirect3D)
                    {
                        devicePtr = GetDirect3d11Device(pipelinePtr);
                        contextPtr = GetDirect3d11DeviceContext(pipelinePtr);
                    }

                    status =
                        "Rhino WIP D3D preview probe" + Environment.NewLine
                        + "timestamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + Environment.NewLine
                        + "rhino_version=" + rhinoVersion.ToString(CultureInfo.InvariantCulture) + Environment.NewLine
                        + "using_direct3d=" + usingDirect3D.ToString(CultureInfo.InvariantCulture) + Environment.NewLine
                        + "display_pipeline_ptr=0x" + pipelinePtr.ToInt64().ToString("X", CultureInfo.InvariantCulture) + Environment.NewLine
                        + "d3d11_device_ptr=0x" + devicePtr.ToInt64().ToString("X", CultureInfo.InvariantCulture) + Environment.NewLine
                        + "d3d11_context_ptr=0x" + contextPtr.ToInt64().ToString("X", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                status =
                    "Rhino WIP D3D preview probe failed" + Environment.NewLine
                    + "timestamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + Environment.NewLine
                    + "exception=" + ex.GetType().FullName + Environment.NewLine
                    + "message=" + ex.Message;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                File.WriteAllText(OutputPath, status + Environment.NewLine);
            }
            catch
            {
                // The probe is diagnostic only. Never let logging affect preview drawing.
            }
        }

        static int SafeRhinoVersion()
        {
            try
            {
                return RhinoApp.ExeVersion;
            }
            catch
            {
                return 0;
            }
        }

        static IntPtr TryGetDisplayPipelinePointer(DisplayPipeline display, RhinoViewport viewport)
        {
            IntPtr pipelinePtr = TryGetNativePointer(display);

            if (pipelinePtr == IntPtr.Zero && viewport != null)
            {
                IntPtr viewportPtr = TryGetNativePointer(viewport);
                if (viewportPtr != IntPtr.Zero)
                {
                    pipelinePtr = CRhinoViewport_DisplayPipeline(viewportPtr);
                }
            }

            return pipelinePtr;
        }

        internal static IntPtr TryGetNativePointer(object target)
        {
            if (target == null) return IntPtr.Zero;

            Type type = target.GetType();
            while (type != null)
            {
                IntPtr fieldPtr = TryGetNativePointerField(target, type, "m_ptr");
                if (fieldPtr != IntPtr.Zero) return fieldPtr;

                fieldPtr = TryGetNativePointerField(target, type, "m_ptr_display_pipeline");
                if (fieldPtr != IntPtr.Zero) return fieldPtr;

                type = type.BaseType;
            }

            string[] memberNames =
            {
                "ConstPointer",
                "NonConstPointer",
                "NonConstPointer_I_KnowWhatImDoing",
                "NonConstViewportPointer",
                "Pointer",
                "NativePointer",
                "UnsafePointer",
                "GetInternalPointer"
            };

            for (int i = 0; i < memberNames.Length; i++)
            {
                IntPtr ptr = TryGetNativePointerProperty(target, memberNames[i]);
                if (ptr != IntPtr.Zero) return ptr;

                ptr = TryGetNativePointerMethod(target, memberNames[i]);
                if (ptr != IntPtr.Zero) return ptr;
            }

            return IntPtr.Zero;
        }

        static IntPtr TryGetNativePointerField(object target, Type type, string name)
        {
            try
            {
                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null || field.FieldType != typeof(IntPtr)) return IntPtr.Zero;
                return (IntPtr)field.GetValue(target);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        static IntPtr TryGetNativePointerProperty(object target, string name)
        {
            try
            {
                PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property == null || property.PropertyType != typeof(IntPtr)) return IntPtr.Zero;
                return (IntPtr)property.GetValue(target, null);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        static IntPtr TryGetNativePointerMethod(object target, string name)
        {
            try
            {
                MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(IntPtr)) return IntPtr.Zero;
                return (IntPtr)method.Invoke(target, null);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        [DllImport("RhinoCore.dll", EntryPoint = "?RhUsingDirect3D@@YA_NXZ", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        static extern bool RhUsingDirect3D();

        [DllImport("RhinoCore.dll", EntryPoint = "?GetDirect3d11Device@CRhinoDisplayPipeline@@QEAAPEAUID3D11Device@@XZ", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr GetDirect3d11Device(IntPtr displayPipeline);

        [DllImport("RhinoCore.dll", EntryPoint = "?GetDirect3d11DeviceContext@CRhinoDisplayPipeline@@QEAAPEAUID3D11DeviceContext1@@XZ", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr GetDirect3d11DeviceContext(IntPtr displayPipeline);

        [DllImport("rhcommon_c.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr CRhinoViewport_DisplayPipeline(IntPtr viewport);
    }
}
