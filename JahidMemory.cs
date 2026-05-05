using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace JahidMemory
{
    #region JahidMem
    public class JahidMemory
    {
        // Cache for addresses to avoid repeated lookups
        private Dictionary<string, UIntPtr> addressCache = new Dictionary<string, UIntPtr>();

        // Fast write methods with optimized operation
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [MarshalAs(UnmanagedType.AsAny)] object lpBuffer, int dwSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern void GetSystemInfo(out JahidMemory.SYSTEM_INFO lpSystemInfo);
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32")]
        public static extern bool IsWow64Process(IntPtr hProcess, out bool lpSystemInfo);
        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtectEx(IntPtr hProcess, UIntPtr lpAddress, IntPtr dwSize, JahidMemory.MemoryProtection flNewProtect, out JahidMemory.MemoryProtection lpflOldProtect);
        [DllImport("kernel32.dll")]
        private static extern bool WriteProcessMemory(IntPtr hProcess, UIntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, IntPtr lpNumberOfBytesWritten);
        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, UIntPtr lpBaseAddress, [Out] byte[] lpBuffer, UIntPtr nSize, IntPtr lpNumberOfBytesRead);
        [DllImport("kernel32.dll")]
        public static extern int CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", EntryPoint = "VirtualQueryEx")]
        public static extern UIntPtr Native_VirtualQueryEx(IntPtr hProcess, UIntPtr lpAddress, out JahidMemory.MEMORY_BASIC_INFORMATION64 lpBuffer, UIntPtr dwLength);
        [DllImport("kernel32.dll", EntryPoint = "VirtualQueryEx")]
        public static extern UIntPtr Native_VirtualQueryEx(IntPtr hProcess, UIntPtr lpAddress, out JahidMemory.MEMORY_BASIC_INFORMATION32 lpBuffer, UIntPtr dwLength);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetPrivateProfileString(string lpAppName, string lpKeyName, string lpDefault, StringBuilder lpReturnedString, uint nSize, string lpFileName);
        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, UIntPtr lpBaseAddress, [Out] IntPtr lpBuffer, UIntPtr nSize, out ulong lpNumberOfBytesRead);

        // WARNING: Extreme speed optimization - no safety checks!
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "WriteProcessMemory")]
        private static extern bool WriteProcessMemoryRaw(IntPtr hProcess, long lpBaseAddress, byte[] buffer, int size, out IntPtr lpNumberOfBytesWritten);

        // Direct memory raw pointer operations (no safety, max speed)
        public bool DirectWriteMemory(long addressLong, int value)
        {
            try
            {
                byte[] buffer = BitConverter.GetBytes(value);
                IntPtr bytesWritten;
                return WriteProcessMemoryRaw(pHandle, addressLong, buffer, buffer.Length, out bytesWritten);
            }
            catch
            {
                return false;
            }
        }

        public int DirectReadMemory(long addressLong)
        {
            try
            {
                byte[] buffer = new byte[4];
                IntPtr bytesRead = IntPtr.Zero;
                if (ReadProcessMemory(pHandle, (UIntPtr)((ulong)addressLong), buffer, (UIntPtr)4, IntPtr.Zero))
                {
                    return BitConverter.ToInt32(buffer, 0);
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        // Fast direct access for pre-cached addresses
        private Dictionary<UIntPtr, byte[]> bufferCache = new Dictionary<UIntPtr, byte[]>();

        // Read multiple memory locations in one pass to reduce overhead
        public Dictionary<UIntPtr, int> BatchReadInts(IEnumerable<UIntPtr> addresses)
        {
            Dictionary<UIntPtr, int> results = new Dictionary<UIntPtr, int>();

            foreach (var address in addresses)
            {
                byte[] buffer = new byte[4];
                if (ReadProcessMemory(pHandle, address, buffer, (UIntPtr)4, IntPtr.Zero))
                {
                    results[address] = BitConverter.ToInt32(buffer, 0);
                }
            }

            return results;
        }

        // Write multiple memory locations in one logical operation
        public bool BatchWriteInts(Dictionary<UIntPtr, int> values)
        {
            bool allSuccess = true;

            foreach (var kvp in values)
            {
                byte[] buffer = BitConverter.GetBytes(kvp.Value);
                if (!WriteProcessMemory(pHandle, kvp.Key, buffer, (UIntPtr)4, IntPtr.Zero))
                {
                    allSuccess = false;
                }
            }

            return allSuccess;
        }

        // Preallocate buffers for commonly accessed memory regions
        public void PreallocateBuffers(IEnumerable<UIntPtr> addresses, int bufferSize = 4)
        {
            foreach (var address in addresses)
            {
                if (!bufferCache.ContainsKey(address))
                {
                    bufferCache[address] = new byte[bufferSize];
                }
            }
        }

        // Ultra-fast read using preallocated buffers
        public int FastReadIntBuffered(UIntPtr address)
        {
            if (!bufferCache.ContainsKey(address))
            {
                bufferCache[address] = new byte[4];
            }

            byte[] buffer = bufferCache[address];
            if (ReadProcessMemory(pHandle, address, buffer, (UIntPtr)4, IntPtr.Zero))
            {
                return BitConverter.ToInt32(buffer, 0);
            }
            return 0;
        }

        // Ultra-fast write using direct buffer manipulation
        public bool FastWriteIntBuffered(UIntPtr address, int value)
        {
            if (!bufferCache.ContainsKey(address))
            {
                bufferCache[address] = new byte[4];
            }

            byte[] buffer = bufferCache[address];
            BitConverter.GetBytes(value).CopyTo(buffer, 0);
            return WriteProcessMemory(pHandle, address, buffer, (UIntPtr)4, IntPtr.Zero);
        }

        // Fast read method for integers (4 bytes)
        public int FastReadInt(UIntPtr address)
        {
            byte[] buffer = new byte[4];
            if (!ReadProcessMemory(pHandle, address, buffer, (UIntPtr)4, IntPtr.Zero))
                return 0;
            return BitConverter.ToInt32(buffer, 0);
        }

        // Fast read method with caching
        public int FastReadInt(string addressStr)
        {
            UIntPtr address = GetCachedAddress(addressStr);
            return FastReadInt(address);
        }

        // Fast write method for integers with direct memory access
        public bool FastWriteInt(UIntPtr address, int value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            return WriteProcessMemory(pHandle, address, buffer, (UIntPtr)4, IntPtr.Zero);
        }

        // Fast write method with caching
        public bool FastWriteInt(string addressStr, int value)
        {
            UIntPtr address = GetCachedAddress(addressStr);
            return FastWriteInt(address, value);
        }

        // Optimized method to swap int values between two addresses
        public bool SwapInts(UIntPtr address1, UIntPtr address2)
        {
            int value1 = FastReadInt(address1);
            int value2 = FastReadInt(address2);

            bool success1 = FastWriteInt(address1, value2);
            bool success2 = FastWriteInt(address2, value1);

            return success1 && success2;
        }

        // Swap int values with caching
        public bool SwapInts(string address1Str, string address2Str)
        {
            UIntPtr address1 = GetCachedAddress(address1Str);
            UIntPtr address2 = GetCachedAddress(address2Str);
            return SwapInts(address1, address2);
        }

        // Get address with caching for faster lookups
        public UIntPtr GetCachedAddress(string addressStr)
        {
            if (addressCache.TryGetValue(addressStr, out UIntPtr cachedAddress))
            {
                return cachedAddress;
            }

            UIntPtr address = GetCode(addressStr, "", 8);
            addressCache[addressStr] = address;
            return address;
        }

        // Fast read method for byte arrays with minimal overhead
        public byte[] FastReadBytes(UIntPtr address, int length)
        {
            byte[] buffer = new byte[length];
            if (!ReadProcessMemory(pHandle, address, buffer, (UIntPtr)length, IntPtr.Zero))
                return null;
            return buffer;
        }

        // Fast write method for byte arrays with minimal overhead
        public bool FastWriteBytes(UIntPtr address, byte[] buffer)
        {
            return WriteProcessMemory(pHandle, address, buffer, (UIntPtr)buffer.Length, IntPtr.Zero);
        }

        // Clear the address cache
        public void ClearAddressCache()
        {
            addressCache.Clear();
        }

        // Optimized AoBScan2 that returns cached results if available
        private List<long> cachedScanResults = null;
        private string lastScanPattern = null;

        public async Task<IEnumerable<long>> OptimizedAoBScan(string search, bool writable = false, bool executable = false)
        {
            // Return cached results if pattern matches
            if (cachedScanResults != null && lastScanPattern == search)
            {
                return cachedScanResults;
            }

            // Do the scan
            var results = await AoBScan2(search, writable, executable);

            // Cache the results
            cachedScanResults = results.ToList();
            lastScanPattern = search;

            return cachedScanResults;
        }

        // Fast direct memory write for specific types
        public bool FastWriteMemory<T>(string addressStr, T value) where T : struct
        {
            try
            {
                UIntPtr address = GetCachedAddress(addressStr);
                int size = Marshal.SizeOf(typeof(T));

                // Create a buffer and copy the structure data to it
                byte[] buffer = new byte[size];
                GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                try
                {
                    Marshal.Copy(handle.AddrOfPinnedObject(), buffer, 0, size);
                }
                finally
                {
                    if (handle.IsAllocated)
                        handle.Free();
                }

                // Write the buffer to process memory
                return WriteProcessMemory(pHandle, address, buffer, (UIntPtr)size, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FastWriteMemory error: {ex.Message}");
                return false;
            }
        }

        // Optimized version of AhReadMeFucker with caching
        public byte[] FastReadMem(string code, long length)
        {
            UIntPtr address = GetCachedAddress(code);
            return FastReadBytes(address, (int)length);
        }

        public string LoadCode(string name, string file)
        {
            StringBuilder stringBuilder = new StringBuilder(1024);
            bool flag = file != "";
            if (flag)
            {
                uint privateProfileString = JahidMemory.GetPrivateProfileString("codes", name, "", stringBuilder, (uint)stringBuilder.Capacity, file);
            }
            else
            {
                stringBuilder.Append(name);
            }
            return stringBuilder.ToString();
        }

        public Task<IEnumerable<long>> AoBScan(long start, long end, string search, bool readable, bool writable, bool executable, string file = "")
        {
            return Task.Run<IEnumerable<long>>(delegate ()
            {
                List<MemoryRegionResult> list = new List<MemoryRegionResult>();
                string text = this.LoadCode(search, file);
                string[] array = text.Split(new char[]
                {
                    ' '
                });
                byte[] aobPattern = new byte[array.Length];
                byte[] mask = new byte[array.Length];
                for (int i = 0; i < array.Length; i++)
                {
                    string text2 = array[i];
                    bool flag = text2 == "??" || (text2.Length == 1 && text2 == "?");
                    if (flag)
                    {
                        mask[i] = 0;
                        array[i] = "0x00";
                    }
                    else
                    {
                        bool flag2 = char.IsLetterOrDigit(text2[0]) && text2[1] == '?';
                        if (flag2)
                        {
                            mask[i] = 240;
                            array[i] = text2[0].ToString() + "0";
                        }
                        else
                        {
                            bool flag3 = char.IsLetterOrDigit(text2[1]) && text2[0] == '?';
                            if (flag3)
                            {
                                mask[i] = 15;
                                array[i] = "0" + text2[1].ToString();
                            }
                            else
                            {
                                mask[i] = byte.MaxValue;
                            }
                        }
                    }
                }
                for (int j = 0; j < array.Length; j++)
                {
                    aobPattern[j] = ((byte)(Convert.ToByte(array[j], 16) & mask[j]));
                }
                JahidMemory.SYSTEM_INFO system_INFO = default(JahidMemory.SYSTEM_INFO);
                JahidMemory.GetSystemInfo(out system_INFO);
                UIntPtr minimumApplicationAddress = system_INFO.minimumApplicationAddress;
                UIntPtr maximumApplicationAddress = system_INFO.maximumApplicationAddress;
                bool flag4 = start < (long)minimumApplicationAddress.ToUInt64();
                if (flag4)
                {
                    start = (long)minimumApplicationAddress.ToUInt64();
                }
                bool flag5 = end > (long)maximumApplicationAddress.ToUInt64();
                if (flag5)
                {
                    end = (long)maximumApplicationAddress.ToUInt64();
                }
                Debug.WriteLine(string.Concat(new string[]
                {
                    "[DEBUG] memory scan starting... (start:0x",
                    start.ToString(this.MSize()),
                    " end:0x",
                    end.ToString(this.MSize()),
                    " time:",
                    DateTime.Now.ToString("h:mm:ss tt"),
                    ")"
                }));
                UIntPtr uintPtr = new UIntPtr((ulong)start);
                JahidMemory.MEMORY_BASIC_INFORMATION memory_BASIC_INFORMATION = default(JahidMemory.MEMORY_BASIC_INFORMATION);
                while (this.VirtualQueryEx(this.pHandle, uintPtr, out memory_BASIC_INFORMATION).ToUInt64() != 0UL && uintPtr.ToUInt64() < (ulong)end && uintPtr.ToUInt64() + (ulong)memory_BASIC_INFORMATION.RegionSize > uintPtr.ToUInt64())
                {
                    bool flag6 = memory_BASIC_INFORMATION.State == 4096U;
                    flag6 &= (memory_BASIC_INFORMATION.BaseAddress.ToUInt64() < maximumApplicationAddress.ToUInt64());
                    flag6 &= ((memory_BASIC_INFORMATION.Protect & 256U) == 0U);
                    flag6 &= ((memory_BASIC_INFORMATION.Protect & 1U) == 0U);
                    flag6 &= (memory_BASIC_INFORMATION.Type == this.MEM_PRIVATE || memory_BASIC_INFORMATION.Type == this.MEM_IMAGE);
                    bool flag7 = flag6;
                    if (flag7)
                    {
                        bool flag8 = (memory_BASIC_INFORMATION.Protect & 2U) > 0U;
                        bool flag9 = (memory_BASIC_INFORMATION.Protect & 4U) > 0U || (memory_BASIC_INFORMATION.Protect & 8U) > 0U || (memory_BASIC_INFORMATION.Protect & 64U) > 0U || (memory_BASIC_INFORMATION.Protect & 128U) > 0U;
                        bool flag10 = (memory_BASIC_INFORMATION.Protect & 16U) > 0U || (memory_BASIC_INFORMATION.Protect & 32U) > 0U || (memory_BASIC_INFORMATION.Protect & 64U) > 0U || (memory_BASIC_INFORMATION.Protect & 128U) > 0U;
                        flag8 &= readable;
                        flag9 &= writable;
                        flag10 &= executable;
                        flag6 &= (flag8 || flag9 || flag10);
                    }
                    bool flag11 = !flag6;
                    if (flag11)
                    {
                        uintPtr = new UIntPtr(memory_BASIC_INFORMATION.BaseAddress.ToUInt64() + (ulong)memory_BASIC_INFORMATION.RegionSize);
                    }
                    else
                    {
                        MemoryRegionResult item2 = new MemoryRegionResult
                        {
                            CurrentBaseAddress = uintPtr,
                            RegionSize = memory_BASIC_INFORMATION.RegionSize,
                            RegionBase = memory_BASIC_INFORMATION.BaseAddress
                        };
                        uintPtr = new UIntPtr(memory_BASIC_INFORMATION.BaseAddress.ToUInt64() + (ulong)memory_BASIC_INFORMATION.RegionSize);
                        bool flag12 = list.Count > 0;
                        if (flag12)
                        {
                            MemoryRegionResult memoryRegionResult = list[list.Count - 1];
                            bool flag13 = (ulong)memoryRegionResult.RegionBase + (ulong)memoryRegionResult.RegionSize == (ulong)memory_BASIC_INFORMATION.BaseAddress;
                            if (flag13)
                            {
                                list[list.Count - 1] = new MemoryRegionResult
                                {
                                    CurrentBaseAddress = memoryRegionResult.CurrentBaseAddress,
                                    RegionBase = memoryRegionResult.RegionBase,
                                    RegionSize = memoryRegionResult.RegionSize + memory_BASIC_INFORMATION.RegionSize
                                };
                                continue;
                            }
                        }
                        list.Add(item2);
                    }
                }
                ConcurrentBag<long> bagResult = new ConcurrentBag<long>();
                Parallel.ForEach<MemoryRegionResult>(list, delegate (MemoryRegionResult item, ParallelLoopState parallelLoopState, long index)
                {
                    long[] array2 = this.CompareScan(item, aobPattern, mask);
                    foreach (long item3 in array2)
                    {
                        bagResult.Add(item3);
                    }
                });
                Debug.WriteLine("[DEBUG] memory scan completed. (time:" + DateTime.Now.ToString("h:mm:ss tt") + ")");
                return (from c in bagResult.ToList<long>()
                        orderby c
                        select c).AsEnumerable<long>();
            });
        }
        public string MSize()
        {
            bool is64Bit = this.Is64Bit;
            string result;
            if (is64Bit)
            {
                result = "x16";
            }
            else
            {
                result = "x8";
            }
            return result;
        }
        public void CloseProcess()
        {
            IntPtr intPtr = this.pHandle;
            bool flag = false;
            if (!flag)
            {
                JahidMemory.CloseHandle(this.pHandle);
                this.theProc = null;
            }
        }
        private bool _is64Bit;
        public bool Is64Bit
        {
            get
            {
                return this._is64Bit;
            }
            private set
            {
                this._is64Bit = value;
            }
        }
        private unsafe long[] CompareScan(MemoryRegionResult item, byte[] aobPattern, byte[] mask)
        {
            bool flag = mask.Length != aobPattern.Length;
            if (flag)
            {
                throw new ArgumentException("aobPattern.Length != mask.Length");
            }
            IntPtr intPtr = Marshal.AllocHGlobal((int)item.RegionSize);
            ulong num;
            JahidMemory.ReadProcessMemory(this.pHandle, item.CurrentBaseAddress, intPtr, (UIntPtr)((ulong)item.RegionSize), out num);
            int num2 = 0 - aobPattern.Length;
            List<long> list = new List<long>();
            do
            {
                num2 = this.FindPattern((byte*)intPtr.ToPointer(), (int)num, aobPattern, mask, num2 + aobPattern.Length);
                bool flag2 = num2 >= 0;
                if (flag2)
                {
                    list.Add((long)((ulong)item.CurrentBaseAddress + (ulong)((long)num2)));
                }
            }
            while (num2 != -1);
            Marshal.FreeHGlobal(intPtr);
            return list.ToArray();
        }
        private unsafe int FindPattern(byte* body, int bodyLength, byte[] pattern, byte[] masks, int start = 0)
        {
            int num = -1;
            bool flag = bodyLength <= 0 || pattern.Length == 0 || start > bodyLength - pattern.Length || pattern.Length > bodyLength;
            int result;
            if (flag)
            {
                result = num;
            }
            else
            {
                for (int i = start; i <= bodyLength - pattern.Length; i++)
                {
                    bool flag2 = (body[i] & masks[0]) == (pattern[0] & masks[0]);
                    if (flag2)
                    {
                        bool flag3 = true;
                        for (int j = 1; j <= pattern.Length - 1; j++)
                        {
                            bool flag4 = (body[i + j] & masks[j]) == (pattern[j] & masks[j]);
                            if (!flag4)
                            {
                                flag3 = false;
                                break;
                            }
                        }
                        bool flag5 = !flag3;
                        if (!flag5)
                        {
                            num = i;
                            break;
                        }
                    }
                }
                result = num;
            }
            return result;
        }
        public UIntPtr VirtualQueryEx(IntPtr hProcess, UIntPtr lpAddress, out JahidMemory.MEMORY_BASIC_INFORMATION lpBuffer)
        {
            bool flag = this.Is64Bit || IntPtr.Size == 8;
            UIntPtr result;
            if (flag)
            {
                JahidMemory.MEMORY_BASIC_INFORMATION64 memory_BASIC_INFORMATION = default(JahidMemory.MEMORY_BASIC_INFORMATION64);
                UIntPtr uintPtr = JahidMemory.Native_VirtualQueryEx(hProcess, lpAddress, out memory_BASIC_INFORMATION, new UIntPtr((uint)Marshal.SizeOf(memory_BASIC_INFORMATION)));
                lpBuffer.BaseAddress = memory_BASIC_INFORMATION.BaseAddress;
                lpBuffer.AllocationBase = memory_BASIC_INFORMATION.AllocationBase;
                lpBuffer.AllocationProtect = memory_BASIC_INFORMATION.AllocationProtect;
                lpBuffer.RegionSize = (long)memory_BASIC_INFORMATION.RegionSize;
                lpBuffer.State = memory_BASIC_INFORMATION.State;
                lpBuffer.Protect = memory_BASIC_INFORMATION.Protect;
                lpBuffer.Type = memory_BASIC_INFORMATION.Type;
                result = uintPtr;
            }
            else
            {
                JahidMemory.MEMORY_BASIC_INFORMATION32 memory_BASIC_INFORMATION2 = default(JahidMemory.MEMORY_BASIC_INFORMATION32);
                UIntPtr uintPtr = JahidMemory.Native_VirtualQueryEx(hProcess, lpAddress, out memory_BASIC_INFORMATION2, new UIntPtr((uint)Marshal.SizeOf(memory_BASIC_INFORMATION2)));
                lpBuffer.BaseAddress = memory_BASIC_INFORMATION2.BaseAddress;
                lpBuffer.AllocationBase = memory_BASIC_INFORMATION2.AllocationBase;
                lpBuffer.AllocationProtect = memory_BASIC_INFORMATION2.AllocationProtect;
                lpBuffer.RegionSize = (long)((ulong)memory_BASIC_INFORMATION2.RegionSize);
                lpBuffer.State = memory_BASIC_INFORMATION2.State;
                lpBuffer.Protect = memory_BASIC_INFORMATION2.Protect;
                lpBuffer.Type = memory_BASIC_INFORMATION2.Type;
                result = uintPtr;
            }
            return result;
        }
        public static void notify(string message)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c start cmd /C \"color b && title Error && echo {message} && timeout /t 5\"")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            Environment.Exit(0);
        }
        public UIntPtr Get64BitCode(string name, string path = "", int size = 16)
        {
            bool flag = path != "";
            string text;
            if (flag)
            {
                text = this.LoadCode(name, path);
            }
            else
            {
                text = name;
            }
            bool flag2 = text == "";
            UIntPtr result;
            if (flag2)
            {
                result = UIntPtr.Zero;
            }
            else
            {
                bool flag3 = text.Contains(" ");
                if (flag3)
                {
                    text.Replace(" ", string.Empty);
                }
                string text2 = text;
                bool flag4 = text.Contains("+");
                if (flag4)
                {
                    text2 = text.Substring(text.IndexOf('+') + 1);
                }
                byte[] array = new byte[size];
                bool flag5 = !text.Contains("+") && !text.Contains(",");
                if (flag5)
                {
                    result = new UIntPtr(Convert.ToUInt64(text, 16));
                }
                else
                {
                    bool flag6 = text2.Contains(',');
                    if (flag6)
                    {
                        List<long> list = new List<long>();
                        string[] array2 = text2.Split(new char[]
                        {
                            ','
                        });
                        foreach (string text3 in array2)
                        {
                            string text4 = text3;
                            bool flag7 = text3.Contains("0x");
                            if (flag7)
                            {
                                text4 = text3.Replace("0x", "");
                            }
                            bool flag8 = !text3.Contains("-");
                            long num;
                            if (flag8)
                            {
                                num = long.Parse(text4, NumberStyles.AllowHexSpecifier);
                            }
                            else
                            {
                                text4 = text4.Replace("-", "");
                                num = long.Parse(text4, NumberStyles.AllowHexSpecifier);
                                num *= -1L;
                            }
                            list.Add(num);
                        }
                        long[] array4 = list.ToArray();
                        bool flag9 = text.Contains("base") || text.Contains("main");
                        if (flag9)
                        {
                            JahidMemory.ReadProcessMemory(this.pHandle, (UIntPtr)((ulong)((long)this.mainModule.BaseAddress + array4[0])), array, (UIntPtr)((ulong)((long)size)), IntPtr.Zero);
                        }
                        else
                        {
                            bool flag10 = !text.Contains("base") && !text.Contains("main") && text.Contains("+");
                            if (flag10)
                            {
                                string[] array5 = text.Split(new char[]
                                {
                                    '+'
                                });
                                IntPtr value = IntPtr.Zero;
                                bool flag11 = !array5[0].ToLower().Contains(".dll") && !array5[0].ToLower().Contains(".exe") && !array5[0].ToLower().Contains(".bin");
                                if (flag11)
                                {
                                    value = (IntPtr)long.Parse(array5[0], NumberStyles.HexNumber);
                                }
                                else
                                {
                                    try
                                    {
                                        value = this.modules[array5[0]];
                                    }
                                    catch
                                    {
                                        Debug.WriteLine("Module " + array5[0] + " was not found in module list!");
                                        Debug.WriteLine("Modules: " + string.Join<KeyValuePair<string, IntPtr>>(",", this.modules));
                                    }
                                }
                                JahidMemory.ReadProcessMemory(this.pHandle, (UIntPtr)((ulong)((long)value + array4[0])), array, (UIntPtr)((ulong)((long)size)), IntPtr.Zero);
                            }
                            else
                            {
                                JahidMemory.ReadProcessMemory(this.pHandle, (UIntPtr)((ulong)array4[0]), array, (UIntPtr)((ulong)((long)size)), IntPtr.Zero);
                            }
                        }
                        long num2 = BitConverter.ToInt64(array, 0);
                        UIntPtr uintPtr = (UIntPtr)0UL;
                        for (int j = 1; j < array4.Length; j++)
                        {
                            uintPtr = new UIntPtr(Convert.ToUInt64(num2 + array4[j]));
                            JahidMemory.ReadProcessMemory(this.pHandle, uintPtr, array, (UIntPtr)((ulong)((long)size)), IntPtr.Zero);
                            num2 = BitConverter.ToInt64(array, 0);
                        }
                        result = uintPtr;
                    }
                    else
                    {
                        // Handle other cases or return zero
                        result = UIntPtr.Zero;
                    }
                }
            }
            return result;
        }

        // Placeholder for missing members from your snippet context
        public IntPtr pHandle { get; set; }
        public Process theProc { get; set; }
        public ProcessModule mainModule { get; set; }
        public Dictionary<string, IntPtr> modules { get; set; }
        public uint MEM_PRIVATE = 0x20000;
        public uint MEM_IMAGE = 0x1000000;

        public struct SYSTEM_INFO { public UIntPtr minimumApplicationAddress; public UIntPtr maximumApplicationAddress; }
        public struct MEMORY_BASIC_INFORMATION { public UIntPtr BaseAddress; public UIntPtr AllocationBase; public uint AllocationProtect; public long RegionSize; public uint State; public uint Protect; public uint Type; }
        public struct MEMORY_BASIC_INFORMATION64 { public UIntPtr BaseAddress; public UIntPtr AllocationBase; public uint AllocationProtect; public uint __alignment1; public ulong RegionSize; public uint State; public uint Protect; public uint Type; public uint __alignment2; }
        public struct MEMORY_BASIC_INFORMATION32 { public UIntPtr BaseAddress; public UIntPtr AllocationBase; public uint AllocationProtect; public uint RegionSize; public uint State; public uint Protect; public uint Type; }
        public enum MemoryProtection : uint { PageReadWrite = 0x04 }
        public struct MemoryRegionResult { public UIntPtr CurrentBaseAddress; public long RegionSize; public UIntPtr RegionBase; }

        // Helper to mimic your original snippet's GetCode
        private UIntPtr GetCode(string name, string file, int size) { return UIntPtr.Zero; }
        private Task<IEnumerable<long>> AoBScan2(string search, bool writable, bool executable) { return Task.FromResult(Enumerable.Empty<long>()); }
    }
    #endregion
}