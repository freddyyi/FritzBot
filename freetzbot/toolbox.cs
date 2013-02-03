﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web;
using Microsoft.CSharp;
using FritzBot.Core;

namespace FritzBot
{
    public static class toolbox
    {
        private static Thread LoggingThread = new Thread(new ThreadStart(LogThread));
        private static Queue<string> LoggingList = new Queue<string>();
        private static readonly object _LogThreadLocker = new object();
        private static readonly object _LoggingLocker = new object();

        private static void LogThread()
        {
            while (true)
            {
                try
                {
                    lock (_LogThreadLocker)
                    {
                        while (!(LoggingList.Count > 0))
                        {
                            Monitor.Wait(_LogThreadLocker);
                        }
                    }
                    FileInfo loginfo = new FileInfo("log.txt");
                    if (loginfo.Exists)
                    {
                        if (loginfo.Length >= 1048576)
                        {
                            if (!Directory.Exists("oldlogs"))
                            {
                                Directory.CreateDirectory("oldlogs");
                            }
                            if (!File.Exists("oldlogs/log" + DateTime.Now.Day + "." + DateTime.Now.Month + ".txt"))
                            {
                                loginfo.MoveTo("oldlogs/log" + DateTime.Now.Day + "." + DateTime.Now.Month + ".txt");
                            }
                        }
                    }
                    File.AppendAllText("log.txt", LoggingList.Peek() + "\r\n", Encoding.GetEncoding("iso-8859-1"));
                    Console.WriteLine(LoggingList.Dequeue());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Fehler beim Zugriff auf den Serverlog: " + ex.Message);
                    return;
                }
            }
        }

        public static void Logging(Exception ex)
        {
            Logging(String.Format("Es ist eine Exception aufgetreten: {0} \r\n {1}", ex.Message, ex.StackTrace));
        }

        public static void Logging(string toLog)
        {
            lock (_LogThreadLocker)
            {
                try
                {
                    if (!LoggingThread.IsAlive)
                    {
                        LoggingThread = new Thread(new ThreadStart(LogThread));
                        LoggingThread.Name = "LoggingThread";
                        LoggingThread.IsBackground = true;
                        LoggingThread.Start();
                    }
                    LoggingList.Enqueue(DateTime.Now.ToString("dd.MM HH:mm:ss ") + toLog);
                    Monitor.Pulse(_LogThreadLocker);
                }
                catch (Exception ex)
                {
                    Logging("Exception beim logging aufgetreten: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Hasht einen String mit dem SHA512 Algorithmus
        /// </summary>
        /// <param name="toCrypt">Der zu hashende String</param>
        /// <returns>Den Hashwert des Strings</returns>
        public static string Crypt(string toCrypt)
        {
            byte[] hash = null;
            byte[] tocode = Encoding.UTF8.GetBytes(toCrypt.ToCharArray());
            using (System.Security.Cryptography.SHA512 theCrypter = new System.Security.Cryptography.SHA512Managed())
            {
                hash = theCrypter.ComputeHash(tocode);
                theCrypter.Clear();
            }
            return BitConverter.ToString(hash).Replace("-", "");
        }

        /// <summary>
        /// Baut unter Verwendung der angegebenen Daten eine neue Verbindung auf
        /// </summary>
        /// <param name="server">Der Hostname des Servers</param>
        /// <param name="Port">Der Port des Servers, standardmäßig 6667</param>
        /// <param name="Nick">Der Nickname des Bots</param>
        /// <param name="Quit_Message">Die Nachricht beim Verlassen</param>
        /// <param name="Channels">Eine Liste von Channelnamen die der Bot betreten soll</param>
        public static void InstantiateConnection(string server, int Port, string Nick, string Quit_Message, string Channel)
        {
            List<string> channels = new List<string>() { { Channel } };
            Server Connection = ServerManager.GetInstance().NewConnection(server, Port, Nick, Quit_Message, channels);
            Connection.Connect();
        }

        /// <summary>
        /// Sendet eine HTTP Anfrage um die gewünschte Webseite in form eines Strings zu erhalten
        /// </summary>
        /// <param name="Url">Die http WebAdresse</param>
        /// <param name="POSTParams">Optionale POST Parameter</param>
        /// <returns>Die Webseite als String</returns>
        public static string GetWeb(string Url, Dictionary<string, string> POSTParams = null)
        {
            string POSTData = "";
            if (POSTParams != null)
            {
                foreach (string key in POSTParams.Keys)
                {
                    POSTData += HttpUtility.UrlEncode(key) + "=" + HttpUtility.UrlEncode(POSTParams[key]) + "&";
                }
            }
            HttpWebResponse response = null;
            HttpWebRequest request = null;
            try
            {
                request = (HttpWebRequest)WebRequest.Create(Url);
                request.Timeout = 10000;
                if (POSTParams != null)
                {
                    request.Method = "POST";
                    request.ContentType = "application/x-www-form-urlencoded";
                    byte[] data = Encoding.UTF8.GetBytes(POSTData);
                    request.ContentLength = data.Length;
                    Stream requestStream = request.GetRequestStream();
                    requestStream.Write(data, 0, data.Length);
                    requestStream.Close();
                }
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (Exception ex)
            {
                Logging("Exception beim Webseiten Aufruf aufgetreten: " + ex.Message);
                return "";
            }
            string theEncoding = "UTF-8";
            if (!String.IsNullOrEmpty(response.CharacterSet))
            {
                theEncoding = response.CharacterSet;
            }
            StreamReader resStream = new StreamReader(response.GetResponseStream(), Encoding.GetEncoding(theEncoding, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback));
            string thepage = resStream.ReadToEnd();
            resStream.Close();
            response.Close();

            return thepage;
        }

        /// <summary>
        /// Kürzt eine URL bei TinyURL
        /// </summary>
        /// <param name="Url">Die zu kürzende URL</param>
        /// <returns>Die gekürzte URL</returns>
        public static string ShortUrl(string Url)
        {
            try
            {
                return GetWeb("http://tinyurl.com/api-create.php?url=" + Url);
            }
            catch
            {
                return Url;
            }
        }

        /// <summary>
        /// Codiert den String entsprechend den Anforderungen einer URL
        /// </summary>
        /// <param name="url">Der zu Codierende String</param>
        /// <returns>Einen URL Encodierten String</returns>
        public static string UrlEncode(string url)
        {
            return System.Web.HttpUtility.UrlEncode(url, Encoding.UTF8);
        }

        public static bool IsOp(string Nickname)
        {
            if (UserManager.GetInstance().Exists(Nickname))
            {
                if (UserManager.GetInstance()[Nickname].IsOp && (UserManager.GetInstance()[Nickname].Authenticated || String.IsNullOrEmpty(UserManager.GetInstance()[Nickname].password)))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsIgnored(string Nickname)
        {
            return UserManager.GetInstance()[Nickname].ignored;
        }

        public static T GetAttribute<T>(object obj)
        {
            return GetAttribute<T>(obj.GetType());
        }

        public static T GetAttribute<T>(Type type)
        {
            Attribute Attr = Attribute.GetCustomAttribute(type, typeof(T));
            if (Attr == null)
            {
                return default(T);
            }
            return (T)(Attr as object);
        }
    }
}