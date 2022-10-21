using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using Microsoft.Win32.SafeHandles;
using System.Threading;

namespace ByeByeChatwork
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string savePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        public string? Token { get; private set; }
        public string? ID { get; private set; }
        public bool IsDownloadFiles { get; private set; }
        public Uri BaseUrl { get; }
        public CookieContainer Cookie { get; }
        public HttpClientHandler Handler { get; }
        public HttpClient Client { get; }

        public ObservableCollection<Room> Rooms { get; set; } = new ObservableCollection<Room>();

        public JToken? Contacts { get; private set; }
        public JToken? RoomsJson { get; private set; }

        public ObservableCollection<LogItem> Logs { get; set; } = new ObservableCollection<LogItem>();

        private LogItem? _log = null;
        public LogItem? LogLine { get => _log; set { _log = value; OnPropertyChanged(); } }

        public string SavePath
        {
            get => savePath; set
            {
                if (System.IO.Directory.Exists(value))
                {
                    savePath = value;
                    OnPropertyChanged();
                }
            }
        }
        public MainWindow()
        {
            InitializeComponent();
            wb.Source = new Uri("https://www.chatwork.com");
            InitAsync();
            BaseUrl = new Uri("https://www.chatwork.com");
            Cookie = new CookieContainer();
            Handler = new HttpClientHandler()
            {
                CookieContainer = Cookie,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 2
            };
            Client = new HttpClient(Handler) { BaseAddress = BaseUrl };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        private async void InitAsync()
        {
            await wb.EnsureCoreWebView2Async();
        }

        private async void Wb_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (wb.CoreWebView2 is not null && string.IsNullOrEmpty(Token) && string.IsNullOrEmpty(ID))
            {
                var html = await wb.ExecuteScriptAsync("document.documentElement.outerHTML;");
                html = Regex.Unescape(html);
                html = html.Remove(0, 1);
                html = html.Remove(html.Length - 1, 1);
                var match = Regex.Match(html, "var ACCESS_TOKEN *= *'(.+)'");
                if (match.Success)
                {
                    Token = match.Groups[1].Value;
                }
                match = Regex.Match(html, "var MYID *= *'(.+)'");
                if (match.Success)
                {
                    ID = match.Groups[1].Value;
                }

                if (!string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(ID))
                {
                    wb.Visibility = Visibility.Collapsed;
                    terminal.Visibility = Visibility.Visible;
                    var c = await wb.CoreWebView2.CookieManager.GetCookiesAsync("");
                    foreach (var co in c)
                    {
                        Cookie.Add(new Cookie(co.Name, co.Value, co.Path, co.Domain));
                    }

                    InitLoad();
                }
            }
        }

        private async void InitLoad()
        {
            var content = new FormUrlEncodedContent(new[]
            {
                //new KeyValuePair<string, string>("email", Email),
                //new KeyValuePair<string, string>("password", Password),
                new KeyValuePair<string, string>("pdata", JsonConvert.SerializeObject( new{ _t = Token }))
            });
            var re = await Client.PostAsync($"/gateway/init_load.php?myid={ID}&_v=1.80a&_av=5&ln=en&with_unconnected_in_organization=1", content);
            if (re.IsSuccessStatusCode)
            {
                var body = await re.Content.ReadAsStringAsync();
                var d = JToken.Parse(body)["result"]!;

                Contacts = d["contact_dat"];
                var RoomsJson = d["room_dat"];

                if (RoomsJson is not null && RoomsJson.Type == JTokenType.Object)
                {
                    var dict = RoomsJson.ToObject<Dictionary<string, JToken>>()!.OrderByDescending(x => x.Value["lt"]!.ToObject<long>()).ToDictionary(x => x.Key, x => x.Value);
                    foreach (var kv in dict!)
                    {
                        var dicmember = kv.Value["m"]!.ToObject<Dictionary<string, JToken>>();
                        var room = new Room
                        {
                            ID = kv.Key
                        };
                        JToken markedMember;
                        if (kv.Value["n"] is null)
                        {
                            foreach (var mem in dicmember!)
                            {
                                if (mem.Key == ID)
                                {
                                    if (kv.Value["tp"]!.ToObject<int>() == 3)
                                    {
                                        room.Name = "mychat";// Contacts![mem.Key]!["nm"]!.ToString();
                                        break;
                                    }
                                    continue;
                                }
                                else
                                {
                                    markedMember = Contacts![mem.Key]!;
                                    room.Name = Regex.Replace(Contacts![mem.Key]!["nm"]!.ToString(), "[[:cntrl:]\\s\\/\\:]", "");
                                    break;
                                }
                            }
                        }
                        else
                        {
                            room.Name = Regex.Replace(kv.Value["n"]!.ToString(), "[[:cntrl:]\\s\\/\\:]", "");
                        }

                        if (kv.Value["ic"] is null)
                        {
                            switch (kv.Value["tp"]!.ToObject<int>())
                            {
                                case 2:
                                    // get other user avatar
                                    room.Icon = "https://appdata.chatwork.com/icon/ico_group.png";
                                    break;
                                case 3:
                                    if (Contacts![ID!] is not null && Contacts![ID!]!["av"] is not null)
                                    {
                                        room.Icon = $"https://appdata.chatwork.com/avatar/{ Contacts![ID!]!["av"] }";
                                    }
                                    else
                                    {
                                        room.Icon = "https://appdata.chatwork.com/icon/ico_group.png";
                                    }
                                    break;
                                default:
                                    room.Icon = "https://appdata.chatwork.com/icon/ico_group.png";
                                    break;
                            }
                        }
                        else
                        {
                            room.Icon = $"https://appdata.chatwork.com/icon/{ kv.Value["ic"]! }";
                        }
                        Rooms.Add(room);
                    }
                }
            }
        }

        private void Wb_Loaded(object sender, RoutedEventArgs e)
        {
            //wb.CoreWebView2.Navigate("https://www.chatwork.com");
        }

        private void CheckDownloadFile_Checked(object sender, RoutedEventArgs e)
        {
            IsDownloadFiles = true;
        }
        private void CheckDownloadFile_Unchecked(object sender, RoutedEventArgs e)
        {
            IsDownloadFiles = false;
        }

        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var item in Rooms)
            {
                item.Checked = true;
            }
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var item in Rooms)
            {
                item.Checked = false;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new FolderPicker
            {
                InputPath = SavePath
            };
            if (dlg.ShowDialog() == true)
            {
                SavePath = dlg.ResultPath;
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            var cb = e.Source as CheckBox;
            if (!cb!.IsChecked.HasValue)
                cb.IsChecked = false;
        }

        private void Downloadbtn_Click(object sender, RoutedEventArgs e)
        {
            var driver = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(SavePath!));
            if (!Rooms.Any(r => r.Checked == true))
            {
                MessageBox.Show("Please select atleast one chat item to backup", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(driver))
            {
                var res = MessageBox.Show("Program cannot get saved local information.\r\n Continue?",
                    "Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (res == MessageBoxResult.Yes)
                {
                    BackUp();
                }
            }
            else
            {
                DriveInfo[] allDrives = DriveInfo.GetDrives();
                if (allDrives.Any(d => driver.StartsWith(d.Name)))
                {
                    var d = allDrives.First(d => driver.StartsWith(d.Name));
                    if (d.DriveFormat == "FAT32" && checkDownloadFile!.IsChecked == true)
                    {
                        var res = MessageBox.Show("This partition cannot save file over 4GB.\r\n Continue?",
                            "Warning",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (res == MessageBoxResult.No)
                        {
                            return;
                        }
                    }

                    var size = d.AvailableFreeSpace / 1073741824.0;
                    if (size < 20.0 && checkDownloadFile!.IsChecked == true)
                    {
                        var res = MessageBox.Show("Freespace is less than 20GB \r\n Continue?",
                            "Warning",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (res == MessageBoxResult.No)
                        {
                            return;
                        }
                    }
                    else if (size < 5.0)
                    {
                        var res = MessageBox.Show("Freespace is less than 5GB \r\n Continue?",
                            "Warning",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (res == MessageBoxResult.No)
                        {
                            return;
                        }
                    }

                    BackUp();
                }
            }
        }

        const int CHAT_SIZE = 40;
        private static readonly Regex _regex = new(@"\\u(?<Value>[a-zA-Z0-9]{4})", RegexOptions.Compiled);
        public static string Decoder(string value)
        {
            try
            {
                return _regex.Replace(
                    value,
                    m => ((char)int.Parse(m.Groups["Value"].Value, System.Globalization.NumberStyles.HexNumber)).ToString()
                );
            }
            catch (Exception)
            {
                return value;
            }

        }
        private async void BackUp()
        {
            bool err = false;
            var listChat = Rooms.Where(r => r.Checked == true);
            await Log("<<>> Begin backup! (=`ω´=)");
            var root = new DirectoryInfo(SavePath);
            foreach (var chat in listChat)
            {
                await Log($">>>> Create chat folder {chat.ID!}_{chat.Name}");

                DirectoryInfo curentdic;

                try
                {
                    curentdic = root.CreateSubdirectory($"{chat.ID}_{System.IO.Path.GetInvalidFileNameChars().Aggregate(chat.Name, (current, c) => current!.Replace(c.ToString(), string.Empty))}");
                }
                catch (Exception)
                {
                    MessageBox.Show("Target folder cannot create or programe don't have enough permission.", "Cannot create directory", MessageBoxButton.OK, MessageBoxImage.Error);
                    await Log("Target folder cannot create or programe don't have enough permission.");
                    err = true;
                    break;
                }
                var filesFolder = curentdic.CreateSubdirectory("files");
                using (var fs = File.CreateText(System.IO.Path.Combine(curentdic.FullName, $"{chat.ID}_description.log")))
                {
                    var res = await LoadChat(chat.ID!);
                    if(res is not null)
                    {
                        try
                        {
                            fs.WriteLine(Decoder(res["description"]!.ToString()));
                        }
                        catch (Exception)
                        {
                            fs.WriteLine(res["description"]!.ToString());
                        }
                    }
                }

                using (var fs = File.CreateText(System.IO.Path.Combine(curentdic.FullName, $"{chat.ID}_{System.IO.Path.GetInvalidFileNameChars().Aggregate(chat.Name, (current, c) => current!.Replace(c.ToString(), string.Empty))}.log")))
                {
                    ulong fid = 0;

                    do
                    {
                        //break;

                        var lc = await LoadOldChat(chat.ID!, fid);
                        if (lc is not null && lc.Count > 0)
                        {
                            foreach (var chatitem in lc)
                            {
                                try
                                {
                                    var msg = Decoder(chatitem["msg"]!.ToString());

                                    var match = Regex.Match(msg, @"\[download\:([^\]]+)\]");
                                    if (match.Success)
                                    {
                                        var fileid = match.Groups[1].Value;
                                        try
                                        {
                                            await DownloadFile(fileid, filesFolder);
                                        }
                                        catch (Exception ex)
                                        {
                                            fs.WriteLine($"Waring: attach file in this message cannot download or skiped (id:{fileid}) :( \r\n{ex.Message}");
                                        }
                                    }

                                    var ac = Contacts![chatitem["aid"]!.ToString()];
                                    var d = DateTimeOffset.FromUnixTimeSeconds(chatitem["tm"]!.ToObject<long>()).ToString("yyyy/MM/dd HH:mm");
                                    var n = chatitem["aid"]!.ToString() + "|" + (ac?["name"]!.ToString() ?? "");
                                    fs.WriteLine();
                                    fs.WriteLine(d + "|" + n);
                                    fs.WriteLine(msg);
                                    fs.WriteLine("------------------------------------");
                                }
                                catch (Exception)
                                {
                                    fs.WriteLine();
                                    fs.WriteLine(chatitem.ToString());
                                    fs.WriteLine("------------------------------------");
                                }
                                //fs.WriteLine($"{d},{ n }, {msg.Replace(",","\",\"")}");
                            }
                            fid = lc.Last()["id"]!.ToObject<ulong>()!;
                        }
                        if ((lc?.Count ?? 0) < CHAT_SIZE)
                        {
                            break;
                        }
                    } while (true);
                }

                await Log($">>>> Done backup {chat.ID!}_{chat.Name}");
            }

            if (!err)
            {
                await Log("<<>> Backup completed! (づ￣ ³￣)づ");
            }
            else
            {
                await Log("<<>> Backup with some error! .｡･ﾟﾟ･(＞_＜)･ﾟﾟ･｡.");
            }
        }

        private async Task DownloadFile(string fileid, DirectoryInfo dir, long limitedLength = 0)
        {
            var re = await Client.GetAsync($"gateway/download_file.php?bin=1&file_id={fileid}");
            if (re.IsSuccessStatusCode)
            {
                var filename = re.Content.Headers.ContentDisposition?.FileName ?? re.Content.Headers.ContentDisposition?.FileNameStar ?? "";

                //var url = re.Headers.GetValues("Location").FirstOrDefault();

                //using (var s = await Client.GetStreamAsync(url))

                long? responseLength = re.Content.Headers.ContentLength;

                if (responseLength.HasValue && responseLength > 1_073_741_824L && checkLimitSize.IsChecked == true)
                {
                    await Log($"<<<< Skiped download file {filename} site: {responseLength}");
                    throw new Exception($"Skiped download file {filename} site: {responseLength}");
                }

                if (limitedLength <= 0 || responseLength is null || limitedLength >= responseLength)
                {
                    var l = await Log($"<<<< Start download file {filename} 0/{responseLength}");

                    using var s = await re.Content.ReadAsStreamAsync();
                    using var fs = new ProgressFileStream(System.IO.Path.Combine(dir.FullName, $"{fileid}_{filename}"), FileMode.CreateNew);
                    fs.FileName = filename;
                    fs.Size = responseLength ?? 0;
                    fs.Log = l;
                    await s.CopyToAsync(fs);

                }
            }
            else
            {
                throw new Exception($"{fileid} cannot download| status {re.StatusCode}");
            }
        }

        private async Task<JToken?> LoadChat(string roomID)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("pdata", JsonConvert.SerializeObject( new{ _t = Token }))
            });
            var re = await Client.PostAsync($"gateway/load_chat.php?myid={ID}&_v=1.80a&_av=5&ln=en&room_id={roomID}&last_chat_id=0&unread_num=0&bookmark=1&file=1&task=1&desc=1", content);
            if (re.IsSuccessStatusCode)
            {
                var body = await re.Content.ReadAsStringAsync();
                var d = JToken.Parse(body)["result"];

                return d;
            }

            return null;
        }

        private async Task<List<JToken>?> LoadOldChat(string roomID, ulong first_chat_id = 0)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("pdata", JsonConvert.SerializeObject( new{ _t = Token }))
            });
            var re = await Client.PostAsync($"gateway/load_old_chat.php?myid={ID}&_v=1.80a&_av=5&ln=en&room_id={roomID}&first_chat_id={first_chat_id}", content);
            if (re.IsSuccessStatusCode)
            {
                var body = await re.Content.ReadAsStringAsync();
                var d = JToken.Parse(body)["result"]!["chat_list"]! as JArray;

                return d?.OrderByDescending(t => t["id"]!.ToObject<ulong>()).ToList() ?? null;
            }

            return null;
        }

        public async Task<LogItem> Log(string mes)
        {
            LogItem log = new() { Text = mes };

            await Dispatcher.InvokeAsync(() =>
            {
                log.Foreground = mes switch
                {
                    string s when s.StartsWith("<<<<") => new SolidColorBrush(Colors.DarkSalmon),
                    string s when s.StartsWith(">>>>") => new SolidColorBrush(Colors.Purple),
                    string s when s.StartsWith("<<>>") => new SolidColorBrush(Colors.Gold),
                    string s when s.StartsWith("Error") => new SolidColorBrush(Colors.Red),
                    _ => new SolidColorBrush(Colors.White),
                };
                Logs.Add(log);

                lslView.ScrollIntoView(log);
            });

            return log;
        }

        private void CheckLimitSize_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckLimitSize_Unchecked(object sender, RoutedEventArgs e)
        {

        }
    }

    public class BindFire : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class Room : BindFire
    {
        private string? name;
        private string? icon;
        private bool @checked = false;

        public string? Name
        {
            get => name; set
            {
                name = value;
                OnPropertyChanged();
            }
        }
        public string? Icon
        {
            get => icon; set
            {
                icon = value;
                OnPropertyChanged();
            }
        }

        public bool Checked
        {
            get => @checked; set
            {
                @checked = value;
                OnPropertyChanged();
            }
        }
        public string? ID { get; set; }
    }

    public class LogItem : BindFire
    {
        private string text = string.Empty;

        public string Text
        {
            get => text; set
            {
                text = value;
                OnPropertyChanged();
            }
        }
        public SolidColorBrush Foreground { get; set; } = new SolidColorBrush(Colors.White);
    }

    public class ProgressFileStream : FileStream
    {
        public LogItem? Log { get; set; } = null;
        public long Size { get; set; } = 0;
        public long Current { get; set; } = 0;
        public string FileName { get; set; } = string.Empty;
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Current += count;
            Log!.Text = $"<<<< Downloaded file {FileName} {Current}/{Size}";
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }
        public ProgressFileStream(SafeFileHandle handle, FileAccess access) : base(handle, access)
        {
        }



        public ProgressFileStream(string path, FileMode mode) : base(path, mode)
        {
        }

        public ProgressFileStream(string path, FileStreamOptions options) : base(path, options)
        {
        }

        public ProgressFileStream(SafeFileHandle handle, FileAccess access, int bufferSize) : base(handle, access, bufferSize)
        {
        }



        public ProgressFileStream(string path, FileMode mode, FileAccess access) : base(path, mode, access)
        {
        }

        public ProgressFileStream(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync) : base(handle, access, bufferSize, isAsync)
        {
        }



        public ProgressFileStream(string path, FileMode mode, FileAccess access, FileShare share) : base(path, mode, access, share)
        {
        }



        public ProgressFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize) : base(path, mode, access, share, bufferSize)
        {
        }

        public ProgressFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync) : base(path, mode, access, share, bufferSize, useAsync)
        {
        }

        public ProgressFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options) : base(path, mode, access, share, bufferSize, options)
        {
        }
    }
}
