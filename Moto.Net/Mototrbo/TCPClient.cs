﻿using Moto.Net.Mototrbo.XNL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Moto.Net.Mototrbo
{
    public class TCPClient : IDisposable
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        protected System.Net.Sockets.TcpClient rawClient;
        protected IPEndPoint ep;
        protected bool running;
        protected bool ignoreUnknown;
        protected Thread thread;
        protected NetworkStream stream;
        protected BlockingCollection<Packet> output;
        private byte[] buffer = new byte[1024];

        public event PacketHandler GotXNLXCMPPacket;

        public TCPClient(IPEndPoint ep) : this(ep, false)
        {
        }

        public TCPClient(IPEndPoint ep, bool ignoreUnknown)
        {
            this.ep = ep;
            this.ignoreUnknown = ignoreUnknown;
            this.rawClient = new System.Net.Sockets.TcpClient();
            this.rawClient.Connect(ep);
            this.stream = this.rawClient.GetStream();
            this.output = new BlockingCollection<Packet>();
            this.running = true;
            this.stream.BeginRead(buffer, 0, buffer.Length, new AsyncCallback(this.GotData), null);
        }

        private void GotData(IAsyncResult result)
        {
            try
            {
                int count = this.stream.EndRead(result);
                if (count > 0)
                {
                    byte[] tmpBuffer = new byte[count];
                    Buffer.BlockCopy(this.buffer, 0, tmpBuffer, 0, count);
                    XNLPacket p = XNLPacket.Decode(tmpBuffer);
                    log.DebugFormat("Recieved {0}", p);
                    XNLXCMPPacket pkt = new XNLXCMPPacket(new RadioID(0), p);
                    PacketEventArgs e = new PacketEventArgs(pkt, this.ep);
                    if (this.GotXNLXCMPPacket != null)
                    {
                        this.GotXNLXCMPPacket(this, e);
                    }
                    else if (!ignoreUnknown)
                    {
                        log.ErrorFormat("Got an unknown packet {0}", p);
                        output.Add(pkt);
                        this.thread = new Thread(this.SendOld);
                        this.thread.Start();
                    }
                }
                this.stream.BeginRead(buffer, 0, buffer.Length, new AsyncCallback(this.GotData), null);
            }
            catch(ObjectDisposedException)
            {
                //TCPClient is being disposed, this is fine, just ignore this exception
                return;
            }
        }

        public void SendOld()
        {
            while (this.GotXNLXCMPPacket == null)
            {
                //Wait for an event listener to register...
                Thread.Sleep(100);
            }
            while (output.Count > 0)
            {
                Packet p = output.Take();
                PacketEventArgs e = new PacketEventArgs(p, this.ep);
                this.GotXNLXCMPPacket(this, e);
            }
        }

        public bool Send(XNLPacket packet)
        {
            byte[] bytes;
            log.DebugFormat("Sending packet {0} to {1}", packet, this.ep);
            bytes = packet.Encode();
            this.stream.Write(bytes, 0, bytes.Length);
            return true;
        }

        public bool RawSend(byte[] bytes)
        {
            this.stream.Write(bytes, 0, bytes.Length);
            return true;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (this.thread != null)
                {
                    this.thread.Abort();
                }
                if (disposing)
                {
                    this.rawClient.Close();
                    this.output.Dispose();
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}
