using Avalonia.Controls;
using Avalonia.Platform;
using System;

namespace SoulJemApp.Controls
{
    public class MpvHost : NativeControlHost
    {
        public IntPtr Xid { get; private set; }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            var handle = base.CreateNativeControlCore(parent);
            Xid = handle.Handle; // Ecco il magico ID della finestra su Linux!
            return handle;
        }
    }
}
