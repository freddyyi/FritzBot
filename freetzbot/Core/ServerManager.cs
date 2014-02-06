﻿using Db4objects.Db4o;
using Meebey.SmartIrc4net;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading;

namespace FritzBot.Core
{
    /// <summary>
    /// Der ServerManger verwaltet die Server
    /// </summary>
    public class ServerManager : IEnumerable<Server>
    {
        private static ServerManager instance;
        private List<Server> _servers;

        /// <summary>
        /// Gibt die Singleton Instanz des ServerManagers zurück
        /// </summary>
        /// <returns></returns>
        public static ServerManager GetInstance()
        {
            Contract.Ensures(Contract.Result<ServerManager>() != null);

            if (instance == null)
            {
                instance = new ServerManager();
            }
            return instance;
        }

        /// <summary>
        /// Gibt den Server mit dem angegebenen Hostname zurück
        /// </summary>
        /// <param name="Hostname">Der Hostname des IRC Servers</param>
        public Server this[string Hostname]
        {
            get
            {
                Contract.Ensures(Contract.Result<Server>() != null);

                foreach (Server oneServer in _servers)
                {
                    if (oneServer.Hostname == Hostname)
                    {
                        return oneServer;
                    }
                }
                throw new ArgumentException("Unknown Server");
            }
        }

        /// <summary>
        /// Erstellt eine neue Instanz des ServerManagers und lädt die Server aus der Datenbank
        /// </summary>
        private ServerManager()
        {
            using (DBProvider db = new DBProvider())
            {
                _servers = db.Query<Server>().ToList();

                //Workaround um dem Bug entgegen zu Wirken, dass Connected = true in die Datenbank gespeichert wird und beim Neustart ConnectAll keinen Effekt hat.
                foreach (Server srv in _servers)
                {
                    srv.Disconnect();
                }
            }
        }

        /// <summary>
        /// Die Anzahl der verwalteten Verbindungen
        /// </summary>
        public int ConnectionCount
        {
            get
            {
                return _servers.Count;
            }
        }

        /// <summary>
        /// Erstellt eine neue IRC Verbindung zu der Verbunden wird und zur Verwaltung hinzugefügt wird
        /// </summary>
        /// <param name="HostName">Der Hostname des Servers z.b. example.com</param>
        /// <param name="Port">Der numerische Port des Servers, der Standard IRC Port ist 6667</param>
        /// <param name="Nickname">Der Nickname den der IRCbot für diese Verbindung verwenden soll</param>
        /// <param name="QuitMessage">Legt die Nachricht beim Verlassen des Servers fest</param>
        /// <param name="Channels">Alle Channels die Betreten werden sollen</param>
        public Server NewConnection(string HostName, int Port, string Nickname, string QuitMessage, List<string> Channels)
        {
            Contract.Ensures(Contract.Result<Server>() != null);

            Server server = new Server()
            {
                Hostname = HostName,
                Port = Port,
                Nickname = Nickname,
                QuitMessage = QuitMessage,
                Channels = Channels
            };
            _servers.Add(server);
            using (DBProvider db = new DBProvider())
            {
                db.SaveOrUpdate(server);
            }
            return server;
        }

        /// <summary>
        /// Trennt die Verbindung zu diesem Server und entfernt ihn aus der Datenbank
        /// </summary>
        /// <param name="server"></param>
        public void Remove(Server server)
        {
            Contract.Requires(server != null);

            server.Disconnect();
            _servers.Remove(server);
            using (DBProvider db = new DBProvider())
            {
                db.Remove(server);
            }
        }

        /// <summary>
        /// Ruft die Connect Methode aller Verwalteten Verbindungen auf
        /// </summary>
        public void ConnectAll()
        {
            foreach (Server theServer in _servers.Where(x => !x.Connected))
            {
                try
                {
                    theServer.Connect();
                }
                catch (Exception ex)
                {
                    toolbox.Logging("Herstellen der Verbindung zu Server " + theServer.Hostname + " fehlgeschlagen");
                    toolbox.Logging(ex);
                }
            }
        }

        /// <summary>
        /// Gibt an ob mindestens ein Server verbunden ist
        /// </summary>
        public bool Connected
        {
            get
            {
                return _servers.Any(x => x.Connected);
            }
        }

        /// <summary>
        /// Trennt die Verbindung zu allen Servern
        /// </summary>
        public void DisconnectAll()
        {
            foreach (Server theServer in _servers.Where(x => x.Connected))
            {
                theServer.Disconnect();
            }
        }

        /// <summary>
        /// Gibt eine Nachricht in allen Channels auf allen Servern bekannt
        /// </summary>
        /// <param name="message">Die Nachricht</param>
        public void AnnounceGlobal(string message)
        {
            foreach (Server theServer in _servers)
            {
                theServer.Announce(message);
            }
        }

        public IEnumerator<Server> GetEnumerator()
        {
            return _servers.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _servers.GetEnumerator();
        }
    }

    /// <summary>
    /// Ein Server kapselt die Verbindung zu einem IRC Server und speichert die Verbindungsdaten
    /// </summary>
    public class Server
    {
        /// <summary>
        /// Wird ausgelöst, wenn ein User einen Channel betritt
        /// </summary>
        public static event JoinEventHandler OnJoin;

        /// <summary>
        /// Wird ausgelöst, wenn ein User einen Channel verlässt
        /// </summary>
        public static event PartEventHandler OnPart;

        /// <summary>
        /// Wird ausgelöst, wenn ein User den IRC Server verlässt
        /// </summary>
        public static event QuitEventHandler OnQuit;

        /// <summary>
        /// Wird ausgelöst, wenn ein User seinen Nickname ändert
        /// </summary>
        public static event NickChangeEventHandler OnNickChange;

        /// <summary>
        /// Wird ausgelöst, wenn ein User gekickt wird
        /// </summary>
        public static event KickEventHandler OnKick;

        /// <summary>
        /// Wird ausgelöst, bevor versucht wird, die Nachricht mit einem Command zu behandeln
        /// </summary>
        public static event IrcMessageEventHandler OnPreProcessingMessage;

        /// <summary>
        /// Wird ausgelöst, nachdem die Nachricht die Verarbeitung durchlaufen hat aber noch vor dem Logging
        /// </summary>
        public static event IrcMessageEventHandler OnPostProcessingMessage;

        public delegate void IrcMessageEventHandler(object sender, ircMessage theMessage);

        /// <summary>
        /// Der Hostname des IRC Servers zu dem die Verbindung aufgebaut wird
        /// </summary>
        public string Hostname { get; set; }

        /// <summary>
        /// Der Port des IRC Servers, üblicherweise 6667
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Der zu verwendende Nickname
        /// </summary>
        public string Nickname { get; set; }

        /// <summary>
        /// Die Nachricht die beim als Grund für das Verlassen des Servers angegeben wird
        /// </summary>
        public string QuitMessage { get; set; }

        /// <summary>
        /// Die Channels, in dem sich der Bot aufhält
        /// </summary>
        public List<string> Channels { get; set; }

        /// <summary>
        /// Gibt an ob eine Verbindung mit dem Server herrgestellt ist
        /// </summary>
        public bool Connected { get; protected set; }

        [Transient]
        private IrcFeatures _connection;

        [Transient]
        private Thread _listener = null;

        /// <summary>
        /// Erstellt ein neues Server Objekt welches die Verbindung zu einem IRC Server kapselt
        /// </summary>
        public Server()
        {
            Channels = new List<string>();
            Connected = false;
        }

        /// <summary>
        /// Betritt den gewünschten Channel und speichert ihn in der Datenbank
        /// </summary>
        /// <param name="channel">Der #channel</param>
        public void JoinChannel(string channel)
        {
            if (!Channels.Contains(channel))
            {
                Channels.Add(channel);
                _connection.RfcJoin(channel);

                using (DBProvider db = new DBProvider())
                {
                    db.SaveOrUpdate(this);
                }
            }
        }

        /// <summary>
        /// Verlässt den Channel und löscht ihn aus der Datenbank
        /// </summary>
        /// <param name="channel">Der #Channel</param>
        public void PartChannel(string channel)
        {
            if (Channels.Contains(channel))
            {
                Channels.Remove(channel);
                _connection.RfcPart(channel);

                using (DBProvider db = new DBProvider())
                {
                    db.SaveOrUpdate(this);
                }
            }
        }

        /// <summary>
        /// Baut eine Verbindung mit den angegebenen Daten auf
        /// </summary>
        public void Connect()
        {
            _connection = new IrcFeatures();

            _connection.ActiveChannelSyncing = true;

            _connection.CtcpSource = "Frag Suchiman in freenode";
            _connection.CtcpUrl = _connection.CtcpSource;
            _connection.CtcpUserInfo = "Ich bin ein automatisch denkendes Wesen auch bekannt als Bot";
            _connection.CtcpVersion = "FritzBot:v3:" + Environment.OSVersion.Platform.ToString();
            _connection.Encoding = Encoding.GetEncoding("iso-8859-1");
            _connection.EnableUTF8Recode = true;

            _connection.OnChannelAction += _connection_OnMessage;
            _connection.OnChannelMessage += _connection_OnMessage;
            _connection.OnChannelNotice += _connection_OnMessage;

            _connection.OnQueryAction += _connection_OnMessage;
            _connection.OnQueryMessage += _connection_OnMessage;
            _connection.OnQueryNotice += _connection_OnMessage;

            _connection.OnNickChange += _connection_OnNickChange;
            _connection.OnJoin += _connection_OnJoin;
            _connection.OnKick += _connection_OnKick;
            _connection.OnPart += _connection_OnPart;
            _connection.OnQuit += _connection_OnQuit;

            _connection.OnConnectionError += _connection_OnConnectionError;

            _connection.Connect(Hostname, Port);
            _connection.Login(Nickname, Nickname, 0, Nickname);

            foreach (string channel in Channels)
            {
                _connection.RfcJoin(channel);
            }

            _listener = toolbox.SafeThreadStart("ListenThread " + Hostname, true, _connection.Listen);

            Connected = true;
        }

        void _connection_OnConnectionError(object sender, EventArgs e)
        {
            toolbox.LogFormat("Verbindung zu Server {0} verloren. Versuche Verbindung wiederherzustellen.", Hostname);
            DateTime TimeConnectionLost = DateTime.Now;

            //Wenn wir bis hierhin gekommen sind, wurde eine bestehende Verbindung aus einem externen Grund terminiert
            //Intern wurde bereits Disconnect aufgerufen, das heißt die Write, Read und Idle Threads wurden beendet.
            //Wir befinden uns in dieser Methode dann im _listener Thread, wenn wir diese Methode verlassen und _connection.IsConnected == true
            //wird die Listen(bool) Methode weiter loopen
            int ConnectionAttempt = 1;
            do
            {
                try
                {
                    _connection.Connect(Hostname, Port);
                    _connection.Login(Nickname, Nickname, 0, Nickname);

                    foreach (string channel in Channels)
                    {
                        _connection.RfcJoin(channel);
                    }
                }
                catch (Exception ex)
                {
                    if (ex is CouldNotConnectException && ex.InnerException != null)
                    {
                        toolbox.LogFormat("Verbindungsversuch zu {0} gescheitert: {1}", Hostname, ex.InnerException.Message);
                    }
                    else
                    {
                        toolbox.LogFormat("Verbindungsversuch zu {0} gescheitert: {1}", Hostname, ex.Message);
                    }
                }

                if (!_connection.IsConnected)
                {
                    ++ConnectionAttempt;
                    int delay = (ConnectionAttempt - 1) % 3 == 0 ? 30000 : 5000;
                    toolbox.LogFormat("Nächster Verbindungsversuch ({0}) zu {1}:{2} in {3} Sekunden", ConnectionAttempt, Hostname, Port, delay / 1000);
                    Thread.Sleep(delay);
                }
            }
            while (!_connection.IsConnected);
            toolbox.LogFormat("Verbindung mit {0} nach {1} Sekunden ohne Verbindung und {2} Verbindungsversuchen wiederhergestellt.", Hostname, DateTime.Now.Subtract(TimeConnectionLost).TotalSeconds, ConnectionAttempt);
        }

        /// <summary>
        /// Stellt sicher, dass der User in der Datenbank für die Anforderung existiert und mit den richtigen Namen ausgestattet ist
        /// </summary>
        /// <param name="Name">Der Nickname des Users</param>
        private User MaintainUser(string Name)
        {
            using (DBProvider db = new DBProvider())
            {
                User u = db.GetUser(Name);
                if (u == null)
                {
                    u = new User();
                    u.Names.Add(Name);
                }
                else if (!u.Names.Contains(Name))
                {
                    u.Names.Add(Name);
                }
                u.LastUsedName = Name;
                db.SaveOrUpdate(u);
                return u;
            }
        }

        /// <summary>
        /// Verarbeitet das Betreten eines Users eines Channels
        /// </summary>
        /// <seealso cref="OnJoin"/>
        private void _connection_OnJoin(object sender, JoinEventArgs e)
        {
            toolbox.LogFormat("{0} hat den Raum {1} betreten", e.Who, e.Channel);
            MaintainUser(e.Who);

            ThreadPool.QueueUserWorkItem(x =>
            {
                var ev = OnJoin;
                if (ev != null)
                {
                    ev(this, e);
                }
            });
        }

        /// <summary>
        /// Verarbeitet das Verlassen eines Users des Servers
        /// </summary>
        /// <seealso cref="OnQuit"/>
        private void _connection_OnQuit(object sender, QuitEventArgs e)
        {
            toolbox.LogFormat("{0} hat den Server verlassen ({1})", e.Who, e.QuitMessage);
            MaintainUser(e.Who);

            ThreadPool.QueueUserWorkItem(x =>
            {
                var ev = OnQuit;
                if (ev != null)
                {
                    ev(this, e);
                }
            });
        }

        /// <summary>
        /// Verarbeitet das Verlassen eines Users eines Channels
        /// </summary>
        /// <seealso cref="OnPart"/>
        private void _connection_OnPart(object sender, PartEventArgs e)
        {
            toolbox.LogFormat("{0} hat den Raum {1} verlassen", e.Who, e.Channel);
            MaintainUser(e.Who);

            ThreadPool.QueueUserWorkItem(x =>
            {
                var ev = OnPart;
                if (ev != null)
                {
                    ev(this, e);
                }
            });
        }

        /// <summary>
        /// Verarbeitet einen Nickname wechsel
        /// </summary>
        /// <seealso cref="OnNickChange"/>
        private void _connection_OnNickChange(object sender, NickChangeEventArgs e)
        {
            toolbox.LogFormat("{0} heißt jetzt {1}", e.OldNickname, e.NewNickname);
            using (DBProvider db = new DBProvider())
            {
                User oldNick = db.GetUser(e.OldNickname);
                User newNick = db.GetUser(e.NewNickname);
                if (oldNick != null && newNick == null)
                {
                    oldNick.Names.Add(e.NewNickname);
                    oldNick.LastUsedName = e.NewNickname;
                    db.SaveOrUpdate(oldNick);
                }
                else if (oldNick == null && newNick != null)
                {
                    newNick.Names.Add(e.OldNickname);
                    newNick.LastUsedName = e.NewNickname;
                    db.SaveOrUpdate(newNick);
                }
            }

            ThreadPool.QueueUserWorkItem(x =>
            {
                var ev = OnNickChange;
                if (ev != null)
                {
                    ev(this, e);
                }
            });
        }

        /// <summary>
        /// Verarbeitet einen Kick
        /// </summary>
        /// <seealso cref="OnKick"/>
        private void _connection_OnKick(object sender, KickEventArgs e)
        {
            toolbox.LogFormat("{0} wurde von {1} aus dem Raum {2} geworfen", e.Who, e.Whom, e.Channel);
            MaintainUser(e.Who);

            ThreadPool.QueueUserWorkItem(x =>
            {
                var ev = OnKick;
                if (ev != null)
                {
                    ev(this, e);
                }
            });
        }

        /// <summary>
        /// Verarbeitet eine IRC Nachricht
        /// </summary>
        /// <seealso cref="OnPreProcessingMessage"/>
        /// <seealso cref="OnPostProcessingMessage"/>
        private void _connection_OnMessage(object sender, IrcEventArgs e)
        {
            if (String.IsNullOrEmpty(e.Data.Nick))
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(x =>
            {
                User user = MaintainUser(e.Data.Nick);
                ircMessage message = new ircMessage(e.Data, this, user);

                if (!message.IsIgnored)
                {
                    var ev = OnPreProcessingMessage;
                    if (ev != null)
                    {
                        ev(this, message);
                    }

                    if (message.IsCommand && !message.HandledByEvent)
                    {
                        try
                        {
                            Program.HandleCommand(message);
                        }
                        catch (Exception ex)
                        {
                            toolbox.Logging(ex);
                        }
                    }

                    ev = OnPostProcessingMessage;
                    if (ev != null)
                    {
                        ev(this, message);
                    }

                    if (message.IsPrivate && !(message.ProcessedByCommand || message.Answered))
                    {
                        if (message.IsCommand)
                        {
                            message.Answer("Dieser Befehl kommt mir nicht bekannt vor... Probiers doch mal mit !hilfe");
                        }
                        else
                        {
                            message.Answer("Hallo du da, ich bin nicht so menschlich wie ich aussehe");
                        }
                    }

                    if (!message.Hidden)
                    {
                        if (message.IsPrivate)
                        {
                            toolbox.Logging("Von " + message.Source + ": " + message.Message);
                        }
                        else
                        {
                            toolbox.Logging(message.Source + " " + message.Nickname + ": " + message.Message);
                        }
                        foreach (string OneMessage in message.UnloggedMessages)
                        {
                            toolbox.Logging(OneMessage);
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Trennt die Verbindung zum Angegebenen Server
        /// </summary>
        public void Disconnect()
        {
            Connected = false;
            if (_connection != null)
            {
                _connection.RfcQuit(QuitMessage);
                if (_listener != null)
                {
                    _listener.Abort();
                }
                if (_connection.IsConnected)
                {
                    _connection.Disconnect();
                    toolbox.Logging("Verbindung zu Server " + Hostname + " getrennt");
                }
                _connection = null;
            }
        }

        /// <summary>
        /// Gibt das der Verbindung zugrunde liegende IRC Objekt zurück
        /// </summary>
        public IrcFeatures IrcClient
        {
            get
            {
                return _connection;
            }
        }

        /// <summary>
        /// Gibt eine Nachricht in allen Channels bekannt
        /// </summary>
        /// <param name="message">Die Nachricht</param>
        public void Announce(string message)
        {
            foreach (string channel in Channels)
            {
                _connection.SendMessage(SendType.Message, channel, message);
            }
        }
    }
}