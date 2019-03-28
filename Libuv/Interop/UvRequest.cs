// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Incubator.Libuv.Internal;
using System;
using System.Runtime.InteropServices;

namespace Incubator.Libuv.Interop
{
    public class UvRequest : UvMemory
    {
        protected UvRequest(ILibuvTrace logger) : base(logger, GCHandleType.Normal)
        {
        }

        public virtual void Init(UvThread thread)
        {
#if DEBUG
            // Store weak handles to all UvRequest objects so we can do leak detection
            // while running tests
            thread.Requests.Add(new WeakReference(this));
#endif
        }

        protected override bool ReleaseHandle()
        {
            DestroyMemory(handle);
            handle = IntPtr.Zero;
            return true;
        }
    }
}

