// SPDX-License-Identifier: MIT
// Protocol and ENC header decoding derived from kagaimiq/jl-uboot-tool and jl-misctools (MIT).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace G7ProXboxWireless
{
    internal sealed class Receiver
    {
        internal string Path, PnpId, UsbId;
        public override string ToString() { return "GameSir BR23 update mode · " + Path; }
    }
    internal static class ReceiverDiscovery
    {
        [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode)] private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
        [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint child, uint flags);
        [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode)] private static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, int length, uint flags);
        internal static List<Receiver> Scan()
        {
            var found = new List<Receiver>();
            using (var search = new ManagementObjectSearcher("SELECT Model,DeviceID,PNPDeviceID FROM Win32_DiskDrive WHERE Model LIKE '%BR23%'") )
            using (var rows = search.Get()) foreach (ManagementObject row in rows)
            {
                string model = Convert.ToString(row["Model"]), pnp = Convert.ToString(row["PNPDeviceID"]), path = Convert.ToString(row["DeviceID"]);
                if (!model.Equals("BR23 UBOOT1.00 USB Device", StringComparison.OrdinalIgnoreCase) || !pnp.StartsWith("USBSTOR\\DISK&VEN_BR23&PROD_UBOOT1.00&", StringComparison.OrdinalIgnoreCase)) continue;
                uint node; if (CM_Locate_DevNodeW(out node, pnp, 0) != 0) continue;
                string usb = null;
                for (int i = 0; i < 6; i++)
                {
                    var id = new StringBuilder(1024);
                    if (CM_Get_Device_IDW(node, id, id.Capacity, 0) == 0 && id.ToString().StartsWith("USB\\VID_4C4A&PID_2342\\", StringComparison.OrdinalIgnoreCase)) { usb = id.ToString(); break; }
                    uint parent; if (CM_Get_Parent(out parent, node, 0) != 0) break; node = parent;
                }
                if (usb != null) found.Add(new Receiver { Path = path, PnpId = pnp, UsbId = usb });
            }
            return found;
        }
        internal static void VerifyOnlyReceiver(Receiver expected)
        {
            var current = Scan();
            Bytes.Require(current.Count == 1 && current[0].Path == expected.Path && current[0].PnpId == expected.PnpId && current[0].UsbId == expected.UsbId, "The selected receiver changed. Connect only the intended receiver and scan again.");
        }
    }

    [StructLayout(LayoutKind.Sequential)] internal struct ScsiPacket
    {
        internal ushort Length;
        internal byte ScsiStatus, PathId, TargetId, Lun, CdbLength, SenseInfoLength, DataIn;
        internal uint DataTransferLength, TimeOutValue;
        internal IntPtr DataBuffer;
        internal uint SenseInfoOffset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=16)] internal byte[] Cdb;
    }
    // This transport exposes no flash erase, flash program, or key-write command.
    internal sealed class BackupLink : IDisposable
    {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
        private readonly SafeFileHandle handle;
        private bool identified, loaded;
        internal const string LoaderHash = "7384429e33ee1f340fbffe46aa928a0099c163e1374a40f6f9f27468a6f16ddf";
        internal const string LoaderUrl = "https://raw.githubusercontent.com/kagaimiq/jl-uboot-tool/adb3f18889e88ac512ce0a3c4d8cc3d3cb30696a/data/loaderblobs/usb/br23loader.bin";
        internal BackupLink(string path)
        {
            handle = CreateFile(path, 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error, "Cannot open receiver. Run this app as administrator, close GameSir apps, and re-enter update mode."); }
        }
        public void Dispose() { handle.Dispose(); }
        internal static void Guard(byte[] c, int length, byte[] output, bool identity, bool reader)
        {
            if (c.Length == 6 && c.SequenceEqual(new byte[] {0x12,0,0,0,36,0}) && length == 36 && output == null) return;
            if (!identity || c.Length != 16) throw new InvalidOperationException("Unidentified target or invalid command length.");
            int cmd = (c[0] << 8) | c[1]; uint address = Bytes.Big32(c, 2); int size = (c[6] << 8) | c[7];
            if (cmd == 0xfb06 && !reader && length == 0 && output != null && size == output.Length && size > 0 && size <= 512 && address >= 0x12000 && (ulong)address + (uint)size <= 0x14c00 && c[8] == 0 && Bytes.U16(c,9) == Bytes.Crc16(output)) return;
            if (cmd == 0xfb08 && !reader && output == null && length == 16 && address == 0x12000 && size == 1) return;
            if (reader && output == null && length == 16 && (cmd == 0xfc0a || cmd == 0xfc0b || cmd == 0xfc14)) return;
            if (reader && cmd == 0xfd05 && output == null && length == size && size > 0 && size <= 256 && (ulong)address + (uint)size <= 0x80000) return;
            throw new InvalidOperationException("Command is outside the backup-only allowlist.");
        }
        private byte[] Transfer(byte[] cdb, int length, byte[] output)
        {
            Guard(cdb, length, output, identified, loaded);
            int count = output == null ? length : output.Length, headerSize = Marshal.SizeOf(typeof(ScsiPacket)), packetSize = headerSize+32;
            IntPtr data = Marshal.AllocHGlobal(Math.Max(count,1)), packet = Marshal.AllocHGlobal(packetSize);
            try
            {
                Marshal.Copy(new byte[Math.Max(count,1)],0,data,Math.Max(count,1)); Marshal.Copy(new byte[packetSize],0,packet,packetSize);
                if (output != null) Marshal.Copy(output,0,data,count);
                var request = new ScsiPacket { Length=(ushort)headerSize, CdbLength=(byte)cdb.Length, SenseInfoLength=32, SenseInfoOffset=(uint)headerSize, DataIn=(byte)(output==null?1:0), DataTransferLength=(uint)count, TimeOutValue=5, DataBuffer=data, Cdb=new byte[16] };
                Buffer.BlockCopy(cdb,0,request.Cdb,0,cdb.Length); Marshal.StructureToPtr(request,packet,false); uint returned;
                if (!DeviceIoControl(handle,0x4d014,packet,(uint)packetSize,packet,(uint)packetSize,out returned,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var actual = (ScsiPacket)Marshal.PtrToStructure(packet,typeof(ScsiPacket));
                Bytes.Require(actual.ScsiStatus==0 && actual.DataTransferLength==count,"Receiver returned a failed or incomplete USB transaction. Replug before retrying.");
                byte[] result = new byte[output==null?length:0]; if(result.Length>0)Marshal.Copy(data,result,0,result.Length); return result;
            }
            finally { Marshal.FreeHGlobal(data); Marshal.FreeHGlobal(packet); }
        }
        internal void Identify()
        {
            byte[] b = Transfer(new byte[]{0x12,0,0,0,36,0},36,null);
            Bytes.Require(Bytes.Text(Bytes.Part(b,8,8)).Trim()=="BR23" && Bytes.Text(Bytes.Part(b,16,16)).Trim()=="UBOOT1.00", "Not a BR23 UBOOT1.00 receiver. No vendor commands sent."); identified=true;
        }
        private byte[] Command(ushort opcode, byte[] args, int length, byte[] output)
        {
            var cdb = Enumerable.Repeat((byte)255,16).ToArray(); cdb[0]=(byte)(opcode>>8); cdb[1]=(byte)opcode; Buffer.BlockCopy(args,0,cdb,2,args.Length);
            byte[] result=Transfer(cdb,length,output);
            if(output==null && opcode!=0xfd05) { Bytes.Require(result[0]==cdb[0] && result[1]==cdb[1],"Unexpected bootloader reply. Unplug/replug and re-enter update mode before retrying."); return Bytes.Part(result,2,result.Length-2); }
            return result;
        }
        internal void LoadReader(byte[] loader)
        {
            Bytes.Require(loader.Length==11264 && Bytes.Hash(loader)==LoaderHash,"Backup reader hash mismatch. No RAM upload performed.");
            for(int offset=0;offset<loader.Length;offset+=512)
            {
                byte[] block=Bytes.Part(loader,offset,512);
                byte[] args=Bytes.Join(Bytes.Big((uint)(0x12000+offset)),new byte[]{2,0,0},BitConverter.GetBytes(Bytes.Crc16(block)));
                Command(0xfb06,args,0,block);
            }
            Command(0xfb08,Bytes.Join(Bytes.Big(0x12000),new byte[]{0,1}),16,null); loaded=true;
            byte[] info=Command(0xfc0a,new byte[0],16,null); uint id=Bytes.U32(info,2);
            Bytes.Require((info[0]==3 || info[0]==0x16) && id==0x856013,"Unsupported flash chip. Only the tested 512 KiB 856013 receiver is supported.");
        }
        internal byte[] Read(uint address) { return Command(0xfd05,Bytes.Join(Bytes.Big(address),new byte[]{1,0}),256,null); }
    }

    internal sealed class FirmwareIdentity
    {
        internal string Product, Version;
        internal int Base;
        internal static FirmwareIdentity Parse(byte[] image)
        {
            Bytes.Require(image.Length==0x80000 && image.Distinct().Take(2).Count()==2,"Expected a nonblank 512 KiB flash image.");
            foreach(int offset in new[]{0,0x1000,0x10000})
            {
                byte[] h=Bytes.Part(image,offset,32); ushort key=65535;
                for(int i=0;i<h.Length;i++) { h[i]^=(byte)key; key=(ushort)((key<<1)^((key&0x8000)!=0?0x1021:0)); }
                if(Bytes.U16(h,0)==0 || Bytes.U16(h,0)!=Bytes.Crc16(Bytes.Part(h,2,30)))continue;
                Bytes.Require(Bytes.U32(h,8)==0x7ff00 && h[12]==2 && h[13]==1,"Unsupported flash layout.");
                return new FirmwareIdentity { Base=offset,Version=Bytes.Text(Bytes.Part(image,offset+4,4)),Product=Bytes.Text(Bytes.Part(image,offset+16,16)) };
            }
            throw new InvalidDataException("Flash header CRC is invalid or the firmware layout is unsupported.");
        }
        internal void RequireStock() { Bytes.Require(Base==0x1000 && Product=="JS_3103_DG" && Version=="1.46","This release supports the tested standard JS_3103_DG firmware 1.46 only. Your image is backed up, but conversion is blocked."); }
    }

    [DataContract] internal sealed class BackupRecord
    {
        [DataMember] public string Schema="g7pro-xbox-backup-v1";
        [DataMember] public string CreatedUtc, DiskPnpId, UsbId, Sha256, Product, Version;
        [DataMember] public int Capacity=0x80000;
        [DataMember] public string Pass1="flash-pass-1.bin",Pass2="flash-pass-2.bin";
        [DataMember] public bool TwoPassMatch;
        [DataMember] public string Scope="Entire addressable SPI NOR including 0x7ff00 reserved tail; excludes OTP/eFuses and other chips. Restoration not tested or included.";
        internal static BackupRecord Load(string manifest)
        {
            BackupRecord r; using(var s=File.OpenRead(manifest)) { Bytes.Require(s.Length<16384,"Invalid backup manifest size."); r=(BackupRecord)new DataContractJsonSerializer(typeof(BackupRecord)).ReadObject(s); }
            Bytes.Require(r.Schema=="g7pro-xbox-backup-v1" && r.TwoPassMatch && r.Capacity==0x80000 && r.Pass1=="flash-pass-1.bin" && r.Pass2=="flash-pass-2.bin","Backup manifest is incomplete or unsupported.");
            string folder=Path.GetDirectoryName(Path.GetFullPath(manifest)); byte[] a=ReadImage(Path.Combine(folder,r.Pass1)),b=ReadImage(Path.Combine(folder,r.Pass2));
            Bytes.Require(a.SequenceEqual(b) && Bytes.Hash(a)==r.Sha256,"Backup copies differ or fail their SHA-256 check.");
            var id=FirmwareIdentity.Parse(a); Bytes.Require(id.Product==r.Product && id.Version==r.Version,"Manifest identity differs from its flash data."); id.RequireStock();
            Bytes.Require(!String.IsNullOrEmpty(r.DiskPnpId) && !String.IsNullOrEmpty(r.UsbId),"Backup receiver identity missing."); return r;
        }
        private static byte[] ReadImage(string path) { Bytes.Require(new FileInfo(path).Length==0x80000,"Backup image size is incorrect."); return File.ReadAllBytes(path); }
        internal void Save(string path) { using(var s=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { new DataContractJsonSerializer(typeof(BackupRecord)).WriteObject(s,this); s.Flush(true); } }
    }
    internal static class BackupService
    {
        internal static BackupRecord Capture(Receiver receiver, byte[] loader, string directory, Action<string,int> progress)
        {
            ReceiverDiscovery.VerifyOnlyReceiver(receiver); Directory.CreateDirectory(directory);
            byte[] first=null, second=null;
            using(var link=new BackupLink(receiver.Path))
            {
                link.Identify(); link.LoadReader(loader);
                for(int pass=1;pass<=2;pass++)
                {
                    using(var file=new FileStream(Path.Combine(directory,"flash-pass-"+pass+".bin"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    using(var memory=new MemoryStream())
                    {
                        for(uint address=0;address<0x80000;address+=256)
                        {
                            byte[] block=link.Read(address); file.Write(block,0,block.Length); memory.Write(block,0,block.Length);
                            if(address%0x10000==0)progress("Reading flash, pass "+pass+" of 2",(int)(((pass-1)*0x80000+address)*100/0x100000));
                        }
                        file.Flush(true); if(pass==1)first=memory.ToArray();else second=memory.ToArray();
                    }
                }
            }
            Bytes.Require(first.SequenceEqual(second),"The two complete reads differ. Conversion stays blocked; replug before retrying.");
            var identity=FirmwareIdentity.Parse(first);
            var record=new BackupRecord {CreatedUtc=DateTime.UtcNow.ToString("O"),DiskPnpId=receiver.PnpId,UsbId=receiver.UsbId,Sha256=Bytes.Hash(first),Product=identity.Product,Version=identity.Version,TwoPassMatch=true};
            record.Save(Path.Combine(directory,"manifest.json"));
            // Reopen and verify durable files rather than trusting only the in-memory comparison.
            var durable=BackupRecord.Load(Path.Combine(directory,"manifest.json")); progress("Two complete reads match. Backup verified.",100); return durable;
        }
    }
}
