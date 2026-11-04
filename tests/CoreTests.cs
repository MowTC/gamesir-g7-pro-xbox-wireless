// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using G7ProXboxWireless;

internal static class CoreTests
{
    private static int checks;
    private static void Check(bool value,string name) { if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name); }
    private static void Reject(Action action,string name) { bool rejected=false;try {action();}catch(Exception e) { if(e is InvalidDataException || e is InvalidOperationException || e is ArgumentException)rejected=true;else throw; }Check(rejected,name); }
    private static byte[] Cdb(int op,uint address,int size)
    {
        var c=Enumerable.Repeat((byte)255,16).ToArray();c[0]=(byte)(op>>8);c[1]=(byte)op;Buffer.BlockCopy(Bytes.Big(address),0,c,2,4);c[6]=(byte)(size>>8);c[7]=(byte)size;return c;
    }
    private static byte[] Enc(byte[] input)
    {
        byte[] result=(byte[])input.Clone();ushort key=65535;
        for(int i=0;i<result.Length;i++) {result[i]^=(byte)key;key=(ushort)((key<<1)^((key&0x8000)!=0?0x1021:0));}return result;
    }
    private static byte[] Image(string product,string version)
    {
        var image=Enumerable.Repeat((byte)255,0x80000).ToArray();var h=new byte[32];Bytes.Put32(h,8,0x7ff00);h[12]=2;h[13]=1;
        var raw=Enc(h);Buffer.BlockCopy(Encoding.ASCII.GetBytes(version),0,raw,4,4);
        for(int i=16;i<32;i++)raw[i]=255;Buffer.BlockCopy(Encoding.ASCII.GetBytes(product),0,raw,16,product.Length);
        h=Enc(raw);Bytes.Put16(h,0,Bytes.Crc16(Bytes.Part(h,2,30)));raw=Enc(h);Buffer.BlockCopy(raw,0,image,0x1000,32);return image;
    }
    private static int Main(string[] args)
    {
        string temp=Path.Combine(Path.GetTempPath(),"G7ProXboxWireless-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            byte[] known=Encoding.ASCII.GetBytes("123456789");Check(Bytes.Crc16(known)==0x31c3,"CRC16 known vector");Check((Bytes.Crc32(known,0xffffffff)^0xffffffff)==0xcbf43926,"CRC32 known vector");Check(Bytes.Adler(known)==0x091e01de,"Adler32 known vector");
            Check(Marshal.SizeOf(typeof(ScsiPacket))==56,"Windows x64 SCSI packet layout");Check(Marshal.OffsetOf(typeof(ScsiPacket),"DataBuffer").ToInt32()==24,"SCSI pointer offset");
            Reject(()=>Bytes.Part(new byte[4],Int32.MaxValue,10),"Reject overflowing file range");
            byte[] data=Enumerable.Range(0,8192).Select(i=>(byte)i).ToArray();byte[] packed=FirmwarePackage.Deflate(data);Check(FirmwarePackage.Inflate(packed).SequenceEqual(data),"Zlib package round trip");
            byte[] corrupt=(byte[])packed.Clone();corrupt[corrupt.Length-1]^=1;Reject(()=>FirmwarePackage.Inflate(corrupt),"Reject bad zlib checksum");
            corrupt=(byte[])packed.Clone();Buffer.BlockCopy(Bytes.Big(0x1000000),0,corrupt,0,4);Reject(()=>FirmwarePackage.Inflate(corrupt),"Reject oversized expanded package");
            var plain=new byte[32];Bytes.Put16(plain,0,32);Bytes.Put16(plain,4,2);Bytes.Put16(plain,16,0x1b19);Bytes.Put16(plain,18,11);Bytes.Put32(plain,20,0xffffffff);
            byte[] record=FirmwarePackage.EncodeRecord(plain,data);Check(FirmwarePackage.CheckRecord(record,data,2).Length==32,"Integrity record round trip");
            corrupt=(byte[])data.Clone();corrupt[5]^=1;Reject(()=>FirmwarePackage.CheckRecord(record,corrupt,2),"Reject payload corruption");Reject(()=>FirmwarePackage.CheckRecord(record,data,1),"Reject wrong record type");
            corrupt=(byte[])record.Clone();corrupt[3]^=1;Reject(()=>FirmwarePackage.DecodeRecord(corrupt),"Reject integrity header corruption");
            Reject(()=>FirmwarePackage.Build(new byte[12]),"Reject unapproved firmware source");Reject(()=>FirmwarePackage.ModifyContainer(new byte[4096]),"Reject malformed embedded file table");
            BackupLink.Guard(new byte[]{0x12,0,0,0,36,0},36,null,false,false);Check(true,"Allow standard INQUIRY before identity");
            foreach(int opcode in new[]{0xfb00,0xfb01,0xfb02,0xfb04,0xfc12})Reject(()=>BackupLink.Guard(Cdb(opcode,0,256),16,null,true,true),"Block flash erase/program/key command "+opcode.ToString("x4"));
            Reject(()=>BackupLink.Guard(Cdb(0xfd05,0,256),256,null,false,true),"Block reads before identity");Reject(()=>BackupLink.Guard(Cdb(0xfd05,0,256),256,null,true,false),"Block flash reads before RAM reader");
            Reject(()=>BackupLink.Guard(Cdb(0xfd05,0x7ff80,256),256,null,true,true),"Block reads beyond physical flash");Reject(()=>BackupLink.Guard(Cdb(0xfd05,0xffffffff,256),256,null,true,true),"Block overflowing flash address");
            BackupLink.Guard(Cdb(0xfd05,0x7ff00,256),256,null,true,true);Check(true,"Include final reserved 256 bytes");
            byte[] block=new byte[512];var write=Cdb(0xfb06,0x12000,512);write[8]=0;Bytes.Put16(write,9,Bytes.Crc16(block));BackupLink.Guard(write,0,block,true,false);Check(true,"Allow bounded RAM reader upload");
            Reject(()=>BackupLink.Guard(write,0,block,true,true),"Block loader overwrite when reader already running");
            write=Cdb(0xfb06,0x11fff,512);write[8]=0;Bytes.Put16(write,9,Bytes.Crc16(block));Reject(()=>BackupLink.Guard(write,0,block,true,false),"Block RAM writes below reader region");
            write=Cdb(0xfb06,0x14b00,512);write[8]=0;Bytes.Put16(write,9,Bytes.Crc16(block));Reject(()=>BackupLink.Guard(write,0,block,true,false),"Block RAM writes beyond reader region");
            Reject(()=>BackupLink.Guard(Cdb(0xfb08,0x12000,0),16,null,true,false),"Block wrong RAM entry argument");
            byte[] image=Image("JS_3103_DG","1.46");var id=FirmwareIdentity.Parse(image);Check(id.Product=="JS_3103_DG" && id.Version=="1.46" && id.Base==0x1000,"Parse stock firmware metadata and CRC");id.RequireStock();Check(true,"Accept tested stock configuration");
            Reject(()=>FirmwareIdentity.Parse(Image("JS_3103_U_DG","5.12")).RequireStock(),"Block already-converted receiver");Reject(()=>FirmwareIdentity.Parse(Image("JS_3103_DG","1.35")).RequireStock(),"Block untested stock version");Reject(()=>FirmwareIdentity.Parse(new byte[0x80000]),"Reject blank flash");
            corrupt=(byte[])image.Clone();corrupt[0x1008]^=1;Reject(()=>FirmwareIdentity.Parse(corrupt),"Reject corrupt firmware header");
            File.WriteAllBytes(Path.Combine(temp,"flash-pass-1.bin"),image);File.WriteAllBytes(Path.Combine(temp,"flash-pass-2.bin"),image);
            var backup=new BackupRecord { CreatedUtc=DateTime.UtcNow.ToString("O"),DiskPnpId="synthetic-disk",UsbId="synthetic-usb",Sha256=Bytes.Hash(image),Product=id.Product,Version=id.Version,TwoPassMatch=true};
            string manifest=Path.Combine(temp,"manifest.json");backup.Save(manifest);Check(BackupRecord.Load(manifest).Sha256==backup.Sha256,"Reload and verify durable backup pair");
            corrupt=(byte[])image.Clone();corrupt[0x5000]^=1;File.WriteAllBytes(Path.Combine(temp,"flash-pass-2.bin"),corrupt);Reject(()=>BackupRecord.Load(manifest),"Reject mismatching backup copies");
            File.WriteAllBytes(Path.Combine(temp,"flash-pass-2.bin"),new byte[10]);Reject(()=>BackupRecord.Load(manifest),"Reject truncated backup");
            if(args.Length==2)
            {
                byte[] original=FirmwarePackage.ReadOfficial(args[0]);byte[] built=FirmwarePackage.Build(original);string output=Path.GetFullPath(args[1]);File.WriteAllBytes(output,built);
                var r=FirmwarePackage.DecodeRecord(Bytes.Part(built,built.Length-64,32));int n=(int)Bytes.U32(r,12);byte[] q=Bytes.Part(built,built.Length-64-n,n);FirmwarePackage.CheckRecord(Bytes.Part(built,built.Length-64,32),q,2);FirmwarePackage.CheckRecord(Bytes.Part(built,built.Length-32,32),Bytes.Part(built,0,built.Length-32),1);
                Check(Bytes.Hash(original)==FirmwarePackage.OfficialHash,"Real official package fingerprint");Check(FirmwarePackage.Inflate(q).Length==2586624,"Real repacked package expands correctly");Check(true,"Both real output integrity records match");Console.WriteLine("Prepared offline fixture: "+output);
            }
            Console.WriteLine(checks+" checks passed. No hardware commands sent.");return 0;
        }
        catch(Exception e) {Console.Error.WriteLine(e);return 1;}
        finally {if(Directory.Exists(temp))Directory.Delete(temp,true);}
    }
}
