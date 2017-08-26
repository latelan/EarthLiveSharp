using System;
using System.Windows.Forms;
using System.Net;
using System.Net.Cache;  
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Drawing;

namespace EarthLiveSharp
{
    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args) 
        {
            if (System.Environment.OSVersion.Version.Major >= 6) { SetProcessDPIAware(); }
            if (File.Exists(Application.StartupPath + @"\trace.log"))
            {
                File.Delete(Application.StartupPath + @"\trace.log");
            }
            Trace.Listeners.Add(new TextWriterTraceListener(Application.StartupPath + @"\trace.log"));
            Trace.AutoFlush = true;

            try
            {
                Cfg.Load();
            }
            catch
            {
                return;
            }
            if (Cfg.source_selection ==0 & Cfg.cloud_name.Equals("demo"))
            {
                #if DEBUG

                #else
                DialogResult dr = MessageBox.Show("WARNING: it's recommended to get images from CDN. \n 注意：推荐使用CDN方式来抓取图片，以提高稳定性。", "EarthLiveSharp");
                if (dr == DialogResult.OK)
                {
                    Process.Start("https://github.com/bitdust/EarthLiveSharp/issues/32");
                }
                #endif
            }
            Cfg.image_folder = Application.StartupPath + @"\images";
            Cfg.Save();
            // scraper.image_source = "http://himawari8-dl.nict.go.jp/himawari8/img/D531106";
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new mainForm());
        }
    }
    public static class scraper
    {
        public const int RETRY_GET_IMAGE = -3;

        public static int size = 1;
        public static string image_folder = "";
        public static string image_tmp_folder = "";
        public static string image_source = "";
        public static int zoom; // max_zoom = 100%
        public static int delete_timeout = 1;
        private static string imageID = "";
        public static string save_imageID = "";
        public static string last_imageID = "0";
        private static string json_url = "http://himawari8.nict.go.jp/img/D531106/latest.json";

        private static int GetImageID()
        {
            HttpWebRequest request = WebRequest.Create(json_url) as HttpWebRequest;
            try 
            {
                HttpWebResponse response = request.GetResponse() as HttpWebResponse;
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("[connection error]");
                }
                if (!response.ContentType.Contains("application/json"))
                {
                    throw new Exception("[no json recieved. your Internet connection is hijacked]");
                }
                StreamReader reader = new StreamReader(response.GetResponseStream());
                string date = reader.ReadToEnd();
                imageID = date.Substring(9,19).Replace("-", "/").Replace(" ", "/").Replace(":", "");
                save_imageID = date.Substring(9, 19).Replace(" ", "-").Replace(":", "-");
                Trace.WriteLine("[get latest ImageID] " + imageID + " now:" + DateTime.Now);
                reader.Close();
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message);
                return -1;
            }
            return 0;
        }

        private static int SaveImage()
        {
            WebClient client = new WebClient();
            try
            {
                for (int ii = 0; ii < size; ii++)
                {
                    for (int jj = 0; jj < size; jj++)
                    {
                        string url = string.Format("{0}/{1}d/550/{2}_{3}_{4}.png", image_source, size, imageID, ii, jj);
                        string image_path = string.Format("{0}\\{1}_{2}.png", image_tmp_folder, ii, jj); // remove the '/' in imageID
                        client.DownloadFile(url, image_path);
                    }
                }
                Trace.WriteLine("[save image] " + save_imageID);
                last_imageID = imageID;
                return 0;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message + " " + imageID);
                Trace.WriteLine(string.Format("[image_folder]{0} [image_source]{1} [size]{2}",image_folder,image_source,size));
                return -1;
            }
        }

        private static void JoinImage()
        {
            // join & convert the images to wallpaper.bmp
            Bitmap bitmap = new Bitmap(550 * size, 550 * size);
            Image[,] tile = new Image[size, size];
            Graphics g = Graphics.FromImage(bitmap);
            for (int ii = 0; ii < size; ii++)
            {
                for (int jj = 0; jj < size; jj++)
                {
                    string image_path = string.Format("{0}\\{1}_{2}.png", image_tmp_folder, ii, jj); // remove the '/' in imageID
                    tile[ii, jj] = Image.FromFile(image_path);
                    g.DrawImage(tile[ii, jj], 550 * ii, 550 * jj);
                    tile[ii, jj].Dispose();
                }
            }
            g.Save();
            g.Dispose();
            if (zoom == 100)
            {
                string image_path = string.Format("{0}\\{1}.png", image_folder, save_imageID);
                string wallpaper_image_path = string.Format("{0}\\wallpaper.png", image_folder);
                bitmap.Save(image_path, System.Drawing.Imaging.ImageFormat.Png);
                bitmap.Save(wallpaper_image_path, System.Drawing.Imaging.ImageFormat.Png);
            }
            else if (1 < zoom & zoom <100)
            {
                string image_path = string.Format("{0}\\{1}.png", image_folder, save_imageID);
                string wallpaper_image_path = string.Format("{0}\\wallpaper.png", image_folder);
                int new_size = bitmap.Height * zoom / 100;
                Bitmap zoom_bitmap = new Bitmap(new_size, new_size);
                Graphics g_2 = Graphics.FromImage(zoom_bitmap);
                g_2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g_2.DrawImage(bitmap, 0, 0, new_size, new_size);
                g_2.Save();
                g_2.Dispose();

                zoom_bitmap.Save(image_path, System.Drawing.Imaging.ImageFormat.Png);
                zoom_bitmap.Save(wallpaper_image_path, System.Drawing.Imaging.ImageFormat.Png);
                zoom_bitmap.Dispose();
            }
            else
            {
                Trace.WriteLine("[zoom error]");
            }
            bitmap.Dispose();
        }

        private static void DeleteOutDateImage()
        {
            // delete all images in the image folder.
            string[] files = Directory.GetFiles(image_folder);
            foreach (string fn in files)
            {
                DateTime date = File.GetCreationTime(fn);
                TimeSpan span = DateTime.Now - date;
                if (fn.CompareTo(string.Format("{0}\\wallpaper.png", image_folder)) == 0)
                {
                    continue;
                }

                if (span.TotalHours > delete_timeout)
                {
                    File.Delete(fn);
                }
            }
        }

        private static void InitFolder()
        {
            if(Directory.Exists(image_folder))
            {
                // delete all images in the image folder.
                //string[] files = Directory.GetFiles(image_folder);
                //foreach (string fn in files)
                //{
                //    File.Delete(fn);
                //}
            }
            else
            {
                Trace.WriteLine("[create folder]");
                Directory.CreateDirectory(image_folder);
            }

            if (Directory.Exists(image_tmp_folder))
            {
                // delete all images in the image folder.
                //string[] files = Directory.GetFiles(image_folder);
                //foreach (string fn in files)
                //{
                //    File.Delete(fn);
                //}
            }
            else
            {
                Trace.WriteLine("[create folder tmp]");
                Directory.CreateDirectory(image_tmp_folder);
            }
        }

        private static void GenLockScreenWallpaper()
        {
            // get current screen bounds
            int SH = Screen.PrimaryScreen.Bounds.Height;
            int SW = Screen.PrimaryScreen.Bounds.Width;

            using (Bitmap wallpaper = new Bitmap(string.Format("{0}\\wallpaper.png", image_folder)),
                          newMap = new Bitmap(SW, SH))
            {
                // we zoom down the wallpaper when it is bigger than the current screen.
                // ratio to 0.8
                int newSize = wallpaper.Height;
                double ratio = 0.8;
                if (newSize >= SH || newSize >= SW)
                {
                    int hX = newSize - SH;
                    int wX = newSize - SW;
                    newSize = (int)((hX > wX) ? (SH * ratio) : (SW * ratio));
                }

                Bitmap newWallpaper = new Bitmap(wallpaper, newSize, newSize);
                Graphics g = Graphics.FromImage(newMap);
                g.Clear(Color.Black);
                g.DrawImage(newWallpaper, (SW - newSize) / 2, (SH - newSize) / 2);
                g.Dispose();
                newWallpaper.Dispose();

                String backgroundImgPath = String.Format("{0}\\backgroundDefault.jpg", image_tmp_folder);
                newMap.Save(backgroundImgPath, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
        }

        public static int UpdateImage()
        {
            InitFolder();
            if (GetImageID() == -1)
            {
                return -1;
            }
            if (imageID.Equals(last_imageID))
            {
                return -2;
            }
            if (SaveImage() == 0)
            {
                DeleteOutDateImage();
                JoinImage();
                GenLockScreenWallpaper();
            }
            else
            {
                //save image fail
                //maybe need retry fast
                return RETRY_GET_IMAGE;
            }
            return 0;
        }
        public static void CleanCDN()
        {
            Cfg.Load();
            if (Cfg.api_key.Length == 0) return;
            if (Cfg.api_secret.Length == 0) return;
            try
            {
                HttpWebRequest request = WebRequest.Create("https://api.cloudinary.com/v1_1/" + Cfg.cloud_name + "/resources/image/fetch?prefix=http://himawari8-dl") as HttpWebRequest;
                request.Method = "DELETE";
                request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
                string svcCredentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(Cfg.api_key + ":" + Cfg.api_secret));
                request.Headers.Add("Authorization", "Basic " + svcCredentials);
                HttpWebResponse response = null;
                StreamReader reader = null;
                string result = null;
                for (int i = 0; i < 3;i++ ) // max 3 request each hour.
                {
                    response = request.GetResponse() as HttpWebResponse;
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception("[clean CND cache connection error]");
                    }
                    if (!response.ContentType.Contains("application/json"))
                    {
                        throw new Exception("[clean CND cache no json recieved. your Internet connection is hijacked]");
                    }
                    reader = new StreamReader(response.GetResponseStream());
                    result = reader.ReadToEnd();
                    if (result.Contains("\"error\""))
                    {
                        throw new Exception("[clean CND cache request error]\n" + result);
                    }
                    if (result.Contains("\"partial\":false"))
                    {
                        Trace.WriteLine("[clean CDN cache done]");
                        break; // end of Clean CDN
                    }
                    else
                    {
                        Trace.WriteLine("[more images to delete]");
                    }
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine("[error when delete CDN cache]");
                Trace.WriteLine(e.Message);
                return;
            }
        }
    }

    public static class Autostart
    {
        static string key = "EarthLiveSharp";
        public static bool Set(bool enabled)
        {
            RegistryKey runKey = null;
            try
            {
                string path = Application.ExecutablePath;
                runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (enabled)
                {
                    runKey.SetValue(key, path);
                }
                else
                {
                    runKey.SetValue(key, path); // dirty fix: to avoid exception in next line.
                    runKey.DeleteValue(key);
                }
                return true;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message);
                return false;
            }
            finally
            {
                if(runKey!=null)
                {
                    runKey.Close();
                }
            }
        }
    }
}
