using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Sims3.SimIFace;

namespace S3IO
{
    public static class ModIO
    {
        private const int BUFFER_SIZE = 16777220; // 16MB + 4 bytes
        private static IntPtr mBuffer = IntPtr.Zero;
        private static readonly object sIpcLock = new object();
        private static unsafe byte* mBufferPtr = null;

        // Command codes matching C++ (Pure File I/O Only)
        private enum Command : ushort
        {
            None = 0,
            File_Exists = 101,
            File_Read = 102,
            File_Write = 103,
            File_Delete = 104,
            Dir_Exists = 105,
            Dir_Create = 106,
            Dir_Delete = 107,
            Dir_ListFiles = 108,
            Dir_ListDirs = 109,
            Sys_GetDocumentsPath = 110,
            File_Append = 111
        }

        // Status codes
        private const byte STATUS_IDLE = 0;
        private const byte STATUS_CS_WRITING = 1;
        private const byte STATUS_READY = 2;
        private const byte STATUS_CPP_PROCESSING = 3;
        private const byte STATUS_DONE = 4;

        static ModIO()
        {
            Initialize();
        }

        public static unsafe bool Initialize()
        {
            lock (sIpcLock)
            {
                if (mBuffer != IntPtr.Zero) return true;

                try
                {
                    mBuffer = Marshal.AllocHGlobal(BUFFER_SIZE);
                    mBufferPtr = (byte*)mBuffer.ToPointer();

                    for (int i = 0; i < 64; i++)
                    {
                        mBufferPtr[i] = 0;
                    }

                    // Write Magic Signature "S3IO_IPC" (8 bytes)
                    mBufferPtr[0] = (byte)'S';
                    mBufferPtr[1] = (byte)'3';
                    mBufferPtr[2] = (byte)'I';
                    mBufferPtr[3] = (byte)'O';
                    mBufferPtr[4] = (byte)'_';
                    mBufferPtr[5] = (byte)'I';
                    mBufferPtr[6] = (byte)'P';
                    mBufferPtr[7] = (byte)'C';

                    mBufferPtr[8] = STATUS_CS_WRITING;
                    return true;
                }
                catch
                {
                    mBuffer = IntPtr.Zero;
                    mBufferPtr = null;
                    return false;
                }
            }
        }

        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static unsafe byte ReadStatus()
        {
            if (mBufferPtr == null) return STATUS_CS_WRITING;
            return mBufferPtr[8];
        }

        public static IntPtr BufferPtr { get { return mBuffer; } }
        public static unsafe byte GetStatusByte() { return ReadStatus(); }

        public static unsafe bool IsConnected
        {
            get
            {
                byte status = ReadStatus();
                return status == STATUS_IDLE || status == STATUS_READY || status == STATUS_CPP_PROCESSING || status == STATUS_DONE;
            }
        }

        private static unsafe bool SendCommand(Command cmd, byte[] payload)
        {
            lock (sIpcLock)
            {
                if (!IsConnected)
                {
                    Initialize();
                    int retries = 0;
                    while (!IsConnected && retries < 50)
                    {
                        Simulator.Sleep(10);
                        retries++;
                    }
                    if (!IsConnected) return false;
                }

                mBufferPtr[8] = STATUS_CS_WRITING;
                *(ushort*)(mBufferPtr + 9) = (ushort)cmd;

                int length = payload != null ? payload.Length : 0;
                *(int*)(mBufferPtr + 11) = length;

                if (payload != null && length > 0)
                {
                    Marshal.Copy(payload, 0, new IntPtr(mBufferPtr + 15), length);
                }

                mBufferPtr[8] = STATUS_READY;

                while (ReadStatus() == STATUS_READY || ReadStatus() == STATUS_CPP_PROCESSING)
                {
                    Simulator.Sleep(10);
                }

                return ReadStatus() == STATUS_DONE;
            }
        }

        private static byte[] EncodeString(string str)
        {
            if (str == null) return new byte[0];
            byte[] utf8 = Encoding.UTF8.GetBytes(str);
            byte[] result = new byte[utf8.Length + 1];
            utf8.CopyTo(result, 0);
            result[utf8.Length] = 0; // null terminator
            return result;
        }

        private static unsafe string DecodeString(byte* ptr, int len)
        {
            if (len <= 0) return string.Empty;
            byte[] bytes = new byte[len];
            Marshal.Copy(new IntPtr(ptr), bytes, 0, len);
            int actualLen = 0;
            while (actualLen < len && bytes[actualLen] != 0) actualLen++;
            return Encoding.UTF8.GetString(bytes, 0, actualLen);
        }

        public static class File
        {
            public static unsafe bool Exists(string path)
            {
                if (string.IsNullOrEmpty(path)) return false;
                byte[] payload = EncodeString(path);
                if (SendCommand(Command.File_Exists, payload))
                {
                    return mBufferPtr[15] != 0;
                }
                return false;
            }

            public static unsafe byte[] ReadAllBytes(string path)
            {
                if (string.IsNullOrEmpty(path)) return null;
                byte[] payload = EncodeString(path);
                if (SendCommand(Command.File_Read, payload))
                {
                    int size = *(int*)(mBufferPtr + 11);
                    if (size < 0) return null;
                    byte[] data = new byte[size];
                    Marshal.Copy(new IntPtr(mBufferPtr + 15), data, 0, size);
                    return data;
                }
                return null;
            }

            public static string ReadAllText(string path)
            {
                byte[] bytes = ReadAllBytes(path);
                if (bytes == null) return null;
                return Encoding.UTF8.GetString(bytes);
            }

            public static unsafe bool WriteAllBytes(string path, byte[] data)
            {
                if (string.IsNullOrEmpty(path) || data == null) return false;
                byte[] pathBytes = EncodeString(path);
                byte[] payload = new byte[4 + pathBytes.Length + data.Length];
                
                BitConverter.GetBytes(pathBytes.Length).CopyTo(payload, 0);
                pathBytes.CopyTo(payload, 4);
                data.CopyTo(payload, 4 + pathBytes.Length);

                return SendCommand(Command.File_Write, payload);
            }

            public static bool WriteAllText(string path, string text)
            {
                if (text == null) text = string.Empty;
                return WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
            }

            public static bool AppendAllBytes(string path, byte[] data)
            {
                if (string.IsNullOrEmpty(path) || data == null) return false;
                byte[] pathBytes = EncodeString(path);
                byte[] payload = new byte[4 + pathBytes.Length + data.Length];
                
                BitConverter.GetBytes(pathBytes.Length).CopyTo(payload, 0);
                pathBytes.CopyTo(payload, 4);
                data.CopyTo(payload, 4 + pathBytes.Length);

                return SendCommand(Command.File_Append, payload);
            }

            public static bool AppendAllText(string path, string text)
            {
                if (text == null) text = string.Empty;
                return AppendAllBytes(path, Encoding.UTF8.GetBytes(text));
            }

            public static bool Delete(string path)
            {
                if (string.IsNullOrEmpty(path)) return false;
                return SendCommand(Command.File_Delete, EncodeString(path));
            }
        }

        public static class Directory
        {
            public static unsafe bool Exists(string path)
            {
                if (string.IsNullOrEmpty(path)) return false;
                if (SendCommand(Command.Dir_Exists, EncodeString(path)))
                {
                    return mBufferPtr[15] != 0;
                }
                return false;
            }

            public static bool Create(string path)
            {
                if (string.IsNullOrEmpty(path)) return false;
                return SendCommand(Command.Dir_Create, EncodeString(path));
            }

            public static bool Delete(string path, bool recursive)
            {
                if (string.IsNullOrEmpty(path)) return false;
                byte[] pathBytes = EncodeString(path);
                byte[] payload = new byte[1 + pathBytes.Length];
                payload[0] = (byte)(recursive ? 1 : 0);
                pathBytes.CopyTo(payload, 1);
                return SendCommand(Command.Dir_Delete, payload);
            }

            public static unsafe List<string> GetFiles(string path)
            {
                if (string.IsNullOrEmpty(path)) return null;
                if (SendCommand(Command.Dir_ListFiles, EncodeString(path)))
                {
                    int offset = 15;
                    int count = *(int*)(mBufferPtr + offset);
                    offset += 4;

                    List<string> list = new List<string>();
                    for (int i = 0; i < count; i++)
                    {
                        int len = *(int*)(mBufferPtr + offset);
                        offset += 4;
                        string file = DecodeString(mBufferPtr + offset, len);
                        list.Add(file);
                        offset += len;
                    }
                    return list;
                }
                return null;
            }

            public static unsafe List<string> GetDirectories(string path)
            {
                if (string.IsNullOrEmpty(path)) return null;
                if (SendCommand(Command.Dir_ListDirs, EncodeString(path)))
                {
                    int offset = 15;
                    int count = *(int*)(mBufferPtr + offset);
                    offset += 4;

                    List<string> list = new List<string>();
                    for (int i = 0; i < count; i++)
                    {
                        int len = *(int*)(mBufferPtr + offset);
                        offset += 4;
                        string dir = DecodeString(mBufferPtr + offset, len);
                        list.Add(dir);
                        offset += len;
                    }
                    return list;
                }
                return null;
            }
        }

        public static class System
        {
            public static unsafe string GetDocumentsPath()
            {
                if (SendCommand(Command.Sys_GetDocumentsPath, null))
                {
                    int size = *(int*)(mBufferPtr + 11);
                    if (size > 0) return DecodeString(mBufferPtr + 15, size);
                }
                return null;
            }
        }
    }
}
