using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace Incubator.SocketServer
{
    internal sealed class SocketAsyncEventArgsPool
    {
        Stack<SocketAsyncEventArgs> pool;
        
        internal SocketAsyncEventArgsPool(int capacity)
        {
            this.pool = new Stack<SocketAsyncEventArgs>(capacity);
        }

        internal int Count
        {
            get { return this.pool.Count; }
        }

        internal SocketAsyncEventArgs Pop()
        {
            lock (this.pool)
            {
                return this.pool.Pop();
            }
        }

        internal void Push(SocketAsyncEventArgs item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("pushµÄitemÎª¿Õ"); 
            }

            lock (this.pool)
            {
                this.pool.Push(item);
            }
        }

        public void Dispose()
        {
            foreach (var item in pool)
            {
                if (item != null)
                {
                    item.Dispose();
                }
            }
        }
    }
}
