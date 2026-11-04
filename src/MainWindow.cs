// SPDX-License-Identifier: MIT
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("G7 Pro Xbox Wireless")]
[assembly: AssemblyDescription("Independent community firmware conversion assistant for supported GameSir G7 Pro receivers")]
[assembly: AssemblyVersion("0.1.1.0")]
[assembly: AssemblyFileVersion("0.1.1.0")]

namespace G7ProXboxWireless
{
    internal sealed class MainWindow
    {
        private readonly Window window;
        private readonly DispatcherTimer timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2) };
        private readonly string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"G7ProXboxWireless");
        private bool busy, scanning, sawAbsent, cycled, launched;
        private DateTime absentSince;
        private byte[] firmware;
        private BackupRecord backup;
        private string backupManifest;
        private Process updater;
        private ComboBox Devices { get { return Find<ComboBox>("Receivers"); } }
        private T Find<T>(string name) where T:class { return (T)window.FindName(name); }
        internal MainWindow()
        {
            using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))window=(Window)XamlReader.Load(s);
            Find<Button>("DownloadFirmware").Click+=(s,e)=>Open(FirmwarePackage.OfficialDownload);
            Find<Button>("OpenSource").Click+=(s,e)=>Open("https://github.com/MowTC/gamesir-g7-pro-xbox-wireless");
            Find<Button>("ChooseFirmware").Click+=async(s,e)=>await ChooseFirmware();
            Find<Button>("Scan").Click+=async(s,e)=>await Scan();
            Find<Button>("Backup").Click+=async(s,e)=>await Capture();
            Find<Button>("Resume").Click+=async(s,e)=>await Resume();
            Find<Button>("Launch").Click+=async(s,e)=>await Launch();
            Find<Button>("OpenBackups").Click+=(s,e)=> { Directory.CreateDirectory(Path.Combine(data,"backups"));Open(Path.Combine(data,"backups")); };
            Find<Button>("Elevate").Click+=(s,e)=>
            {
                try { Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location){UseShellExecute=true,Verb="runas"}); window.Close(); }
                catch(Exception error) { Status("Administrator restart did not complete",error.Message,true); }
            };
            Devices.SelectionChanged+=(s,e)=>Update(); Find<CheckBox>("Acknowledge").Checked+=(s,e)=>Update(); Find<CheckBox>("Acknowledge").Unchecked+=(s,e)=>Update();
            timer.Tick+=async(s,e)=>await Scan();
            window.Loaded+=async(s,e)=> { Update();await Scan();timer.Start(); };
            window.Closing+=(s,e)=>
            {
                if(busy || (updater!=null && !updater.HasExited)) { e.Cancel=true;Status("An operation is still running","Let the backup or updater finish before closing this app.",true); }
                else timer.Stop();
            };
        }
        internal void Show() { window.Show(); }
        private void Open(string path) { try { Process.Start(new ProcessStartInfo(path){UseShellExecute=true}); } catch(Exception e) { Status("Could not open link or folder",e.Message,true); } }
        private bool Admin { get { using(var id=WindowsIdentity.GetCurrent())return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } }
        private void Update()
        {
            Find<Button>("Backup").IsEnabled=!busy && backup==null && Devices.Items.Count==1 && Devices.SelectedItem!=null && !launched;
            Find<Button>("Resume").IsEnabled=!busy && !launched;
            Find<Button>("ChooseFirmware").IsEnabled=!busy && !launched;
            Find<Button>("Scan").IsEnabled=!busy && !scanning;
            Find<Button>("Elevate").Visibility=Admin?Visibility.Collapsed:Visibility.Visible;
            Find<Button>("Elevate").IsEnabled=!busy && !launched;
            Find<Button>("Launch").IsEnabled=!busy && !scanning && !launched && firmware!=null && backup!=null && cycled && Find<CheckBox>("Acknowledge").IsChecked==true && Match(Devices.SelectedItem as Receiver);
        }
        private bool Match(Receiver r) { return r!=null && backup!=null && r.PnpId==backup.DiskPnpId && r.UsbId==backup.UsbId; }
        private void Status(string title,string text,bool error)
        {
            Find<TextBlock>("StatusTitle").Text=title;Find<TextBlock>("StatusText").Text=text;
            Find<Border>("StatusPanel").Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(error?"#482733":"#203048"));
            Log(title+": "+text);
        }
        private void Log(string text)
        {
            if(!window.Dispatcher.CheckAccess()) { window.Dispatcher.BeginInvoke(new Action(()=>Log(text)));return; }
            string line=DateTimeOffset.Now.ToString("O")+" "+text+Environment.NewLine;
            Find<TextBox>("Activity").AppendText(line);Find<TextBox>("Activity").ScrollToEnd();
            try { Directory.CreateDirectory(data);File.AppendAllText(Path.Combine(data,"activity.log"),line); } catch(IOException){} catch(UnauthorizedAccessException){}
        }
        private void Busy(bool value) { busy=value;Find<ProgressBar>("Progress").Visibility=value?Visibility.Visible:Visibility.Collapsed;Devices.IsEnabled=!value;Update(); }
        private async Task Scan()
        {
            if(busy || scanning || launched)return;scanning=true;
            try
            {
                var selected=Devices.SelectedItem as Receiver;var found=await Task.Run(()=>ReceiverDiscovery.Scan());
                Devices.ItemsSource=found;Devices.SelectedItem=found.FirstOrDefault(r=>selected!=null && r.PnpId==selected.PnpId)??(found.Count==1?found[0]:null);
                if(backup!=null)
                {
                    if(found.Count==0)
                    {
                        if(!sawAbsent) { absentSince=DateTime.UtcNow;sawAbsent=true; }
                        cycled=false;
                        Find<TextBlock>("ConversionStatus").Text="Receiver disconnected. Wait at least 3 seconds, reconnect to the same port, and enter update mode again.";
                    }
                    else if(found.Count==1 && Match(found[0]) && sawAbsent && DateTime.UtcNow-absentSince>=TimeSpan.FromSeconds(3))
                    {
                        cycled=true;
                        Find<TextBlock>("ConversionStatus").Text="Replug detected on the backed-up receiver’s USB port. Ready to open the conversion updater. Keep only this receiver connected.";
                    }
                    else if(found.Count>1 || !Match(found[0])) { cycled=false;Find<TextBlock>("ConversionStatus").Text="Receiver does not match the saved USB connection, or multiple receivers are present. Use the original port and receiver."; }
                }
            }
            catch(Exception e) { Log("Receiver scan: "+e.Message); }
            finally { scanning=false;Update(); }
        }
        private async Task ChooseFirmware()
        {
            var dialog=new OpenFileDialog {Title="Select the original official Wuchang Dongle 5.12 updater",Filter="Official updater (*.exe)|*.exe",CheckFileExists=true};
            if(dialog.ShowDialog(window)!=true)return;
            Busy(true);firmware=null;
            try { firmware=await Task.Run(()=>FirmwarePackage.ReadOfficial(dialog.FileName));Find<TextBlock>("FirmwareStatus").Text="Verified original Wuchang dongle 5.12 updater · SHA-256 matches tested package";Status("Official firmware verified","The input file matches the exact tested vendor package. No firmware has been installed.",false); }
            catch(Exception e) { Find<TextBlock>("FirmwareStatus").Text="Firmware rejected";Status("Cannot use this updater",e.Message,true); }
            finally { Busy(false); }
        }
        private byte[] GetReader()
        {
            string folder=Path.Combine(data,"cache"),path=Path.Combine(folder,"br23loader.bin");byte[] loader;
            if(File.Exists(path)) { Bytes.Require(new FileInfo(path).Length==11264,"Cached backup reader size is invalid. Remove the cache file and retry.");loader=File.ReadAllBytes(path); }
            else
            {
                Log("Downloading pinned RAM backup reader from raw.githubusercontent.com. No device data is uploaded.");
                ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
                var request=(HttpWebRequest)WebRequest.Create(BackupLink.LoaderUrl);request.Timeout=20000;request.ReadWriteTimeout=20000;request.AllowAutoRedirect=false;
                using(var response=(HttpWebResponse)request.GetResponse())
                using(var stream=response.GetResponseStream())
                using(var output=new MemoryStream())
                {
                    Bytes.Require(response.StatusCode==HttpStatusCode.OK,"Backup reader download failed.");byte[] block=new byte[4096];int n;
                    while((n=stream.Read(block,0,block.Length))>0) { Bytes.Require(output.Length+n<=11264,"Backup reader download exceeds its expected size.");output.Write(block,0,n); }
                    loader=output.ToArray();
                }
                Bytes.Require(loader.Length==11264 && Bytes.Hash(loader)==BackupLink.LoaderHash,"Downloaded backup reader hash mismatch.");
                Directory.CreateDirectory(folder);File.WriteAllBytes(path,loader);
            }
            Bytes.Require(loader.Length==11264 && Bytes.Hash(loader)==BackupLink.LoaderHash,"Cached backup reader is invalid. Remove the cache file and retry.");return loader;
        }
        private async Task Capture()
        {
            if(!Admin) { Status("Administrator access required","Use Restart as administrator, then select the receiver again. Windows may ask you to approve the restart.",true);return; }
            var receiver=Devices.SelectedItem as Receiver;if(receiver==null)return;
            Busy(true);string folder=Path.Combine(data,"backups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
            try
            {
                Find<ProgressBar>("Progress").Value=0;
                backup=await Task.Run(()=>BackupService.Capture(receiver,GetReader(),folder,(text,n)=>window.Dispatcher.BeginInvoke(new Action(()=> {Find<TextBlock>("StatusTitle").Text=text;Find<ProgressBar>("Progress").Value=n;}))));
                backupManifest=Path.Combine(folder,"manifest.json");
                sawAbsent=cycled=false;Find<TextBlock>("BackupStatus").Text="Verified two-pass backup · "+backup.Product+" "+backup.Version+" · "+folder;
                Status("Full flash backup verified","Two 512 KiB copies match. Now unplug the receiver for at least 3 seconds, reconnect to the same port, and enter update mode again.",false);
            }
            catch(Exception e) { backup=null;Status("Backup did not unlock conversion",e.Message+" Any completed files remain in "+folder+". Replug before retrying.",true); }
            finally { Busy(false); }
        }
        private async Task Resume()
        {
            var dialog=new OpenFileDialog {Title="Select this receiver’s verified backup manifest",Filter="Backup manifest (manifest.json)|manifest.json",InitialDirectory=Path.Combine(data,"backups")};
            if(dialog.ShowDialog(window)!=true)return;Busy(true);backup=null;
            try { backup=await Task.Run(()=>BackupRecord.Load(dialog.FileName));backupManifest=dialog.FileName;sawAbsent=cycled=false;Find<TextBlock>("BackupStatus").Text="Verified saved backup · "+backup.Product+" "+backup.Version;Status("Backup rechecked","Both complete files match their SHA-256. Replug this receiver in the original port while this app stays open, then enter update mode again.",false); }
            catch(Exception e) { Status("Backup rejected",e.Message,true); }
            finally { Busy(false); }
        }
        private async Task Launch()
        {
            var receiver=Devices.SelectedItem as Receiver;
            if(firmware==null || backup==null || !cycled || !Match(receiver) || Find<CheckBox>("Acknowledge").IsChecked!=true)return;
            Busy(true);
            try
            {
                var durable=BackupRecord.Load(backupManifest);
                Bytes.Require(durable.Sha256==backup.Sha256 && durable.DiskPnpId==backup.DiskPnpId && durable.UsbId==backup.UsbId,"Saved backup identity changed. Conversion blocked.");
                ReceiverDiscovery.VerifyOnlyReceiver(receiver);
                // Only standard INQUIRY is sent here; no loader is uploaded before launching the vendor tool.
                using(var link=new BackupLink(receiver.Path))link.Identify();
                string folder=Path.Combine(data,"prepared",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                byte[] built=await Task.Run(()=>FirmwarePackage.Build(firmware));string path=Path.Combine(folder,FirmwarePackage.UpdaterName);
                using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {file.Write(built,0,built.Length);file.Flush(true);}
                Bytes.Require(Bytes.Hash(File.ReadAllBytes(path))==Bytes.Hash(built),"Prepared updater failed disk verification.");
                File.WriteAllText(Path.Combine(folder,"preparation.txt"),"Source SHA256: "+FirmwarePackage.OfficialHash+"\r\nPrepared SHA256: "+Bytes.Hash(built)+"\r\nBackup SHA256: "+backup.Sha256+"\r\nChange: PidMatch/enable true -> false; package CRCs and integrity records regenerated. All UFW bytes unchanged. EraseAll, RewriteAuthCode, AutoUpgrade remain false.\r\n");
                ReceiverDiscovery.VerifyOnlyReceiver(receiver);
                BackupRecord.Load(backupManifest);
                updater=Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=folder});launched=true;timer.Stop();
                Status("Conversion updater opened — installation not yet confirmed","In the vendor window, choose Install Firmware and wait for green Upgrade SUCCESS. Do not unplug during writing. This app does not infer success from the updater closing. Then follow step 04.",false);
                Find<TextBlock>("ConversionStatus").Text="Updater opened. Wait for its explicit Upgrade SUCCESS result. Original backup stays in your backups folder.";
            }
            catch(Exception e) { Status("Conversion updater was not started",e.Message,true); }
            finally { Busy(false); }
        }
    }
    internal static class Program
    {
        [STAThread] private static void Main()
        {
            try { var app=new Application();new MainWindow().Show();app.Run(); }
            catch(Exception e) { MessageBox.Show(e.Message,"G7 Pro Xbox Wireless",MessageBoxButton.OK,MessageBoxImage.Error); }
        }
    }
}
