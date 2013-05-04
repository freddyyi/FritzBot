﻿using Db4objects.Db4o.Ext;
using FritzBot.Core;
using FritzBot.DataModel;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace FritzBot
{
    public class Program
    {
        public static bool restart;

        public static SimpleStorage BotSettings;
        private static int AntiFloodingCount;
        private static bool FloodingNotificated;

        /// <summary>
        /// Verarbeitet eine ircMessage mit einem Command und stellt sicher, dass die dem Command zugeordneten Attribute eingehalten werden
        /// </summary>
        /// <param name="theMessage">Di zu verarbeitende ircMessage</param>
        public static void HandleCommand(ircMessage theMessage)
        {
            #region Antiflooding checks
            if (!toolbox.IsOp(theMessage.TheUser))
            {
                if (AntiFloodingCount >= BotSettings.Get("FloodingCount", 10))
                {
                    if (FloodingNotificated == false)
                    {
                        FloodingNotificated = true;
                        theMessage.Answer("Flooding Protection aktiviert");
                    }
                    return;
                }
                else
                {
                    AntiFloodingCount++;
                }
            }
            #endregion

            try
            {
                ICommand theCommand = PluginManager.GetInstance().Get<ICommand>(theMessage.CommandName);

                if (theCommand != null)
                {
                    bool OPNeeded = toolbox.GetAttribute<Module.AuthorizeAttribute>(theCommand) != null;
                    short ParameterNeeded = 0;
                    Module.ParameterRequiredAttribute ParameterAttri = toolbox.GetAttribute<Module.ParameterRequiredAttribute>(theCommand);
                    if (ParameterAttri != null)
                    {
                        if (ParameterAttri.Required)
                        {
                            ParameterNeeded = 1;
                        }
                        else
                        {
                            ParameterNeeded = 2;
                        }
                    }

                    if (!OPNeeded || toolbox.IsOp(theMessage.TheUser))
                    {
                        if (ParameterNeeded == 0 || (ParameterNeeded == 1 && theMessage.HasArgs) || (ParameterNeeded == 2 && !theMessage.HasArgs))
                        {
                            try
                            {
                                theCommand.Run(theMessage);
                            }
                            catch (Exception ex)
                            {
                                toolbox.Logging("Das Plugin " + toolbox.GetAttribute<Module.NameAttribute>(theCommand).Names[0] + " hat eine nicht abgefangene Exception ausgelöst: " + ex.Message + "\r\n" + ex.StackTrace);
                                theMessage.Answer("Oh... tut mir leid. Das Plugin hat einen internen Ausnahmefehler ausgelöst");
                            }
                            theMessage.ProcessedByCommand = true;
                        }
                        else
                        {
                            theMessage.Answer("Ungültiger Aufruf: " + toolbox.GetAttribute<Module.HelpAttribute>(theCommand).Help);
                        }
                    }
                    else if (theMessage.TheUser.Admin)
                    {
                        theMessage.Answer("Du musst dich erst authentifizieren, " + theMessage.Nickname);
                    }
                    else
                    {
                        theMessage.Answer("Du bist nicht dazu berechtigt, diesen Befehl auszuführen, " + theMessage.Nickname);
                    }
                }
            }
            catch (Exception ex)
            {
                toolbox.Logging("Eine Exception ist beim Ausführen eines Befehles abgefangen worden: " + ex.Message);
            }
        }

        private static void AntiFlooding()
        {
            while (true)
            {
                Thread.Sleep(BotSettings.Get("FloodingCountReduction", 1000));
                if (AntiFloodingCount > 0)
                {
                    AntiFloodingCount--;
                }
                if (AntiFloodingCount == 0)
                {
                    FloodingNotificated = false;
                }
            }
        }

        private static void HandleConsoleInput()
        {
            while (true)
            {
                string ConsoleInput = Console.ReadLine();
                string[] ConsoleSplitted = ConsoleInput.Split(new string[] { " " }, 2, StringSplitOptions.None);
                switch (ConsoleSplitted[0])
                {
                    case "op":
                        using (DBProvider db = new DBProvider())
                        {
                            User nutzer = db.GetUser(ConsoleSplitted[1]);
                            if (nutzer != null)
                            {
                                if (nutzer.Admin)
                                {
                                    Console.WriteLine(nutzer.Names.ElementAt(0) + " ist bereits OP");
                                    break;
                                }
                                nutzer.Admin = true;
                                db.SaveOrUpdate(nutzer);
                                Console.WriteLine(nutzer.Names.ElementAt(0) + " zum OP befördert");
                            }
                            else
                            {
                                Console.WriteLine("Benutzer " + ConsoleSplitted[1] + " nicht gefunden");
                            }
                        }
                        break;
                    case "exit":
                        ServerManager.GetInstance().DisconnectAll();
                        Environment.Exit(0);
                        break;
                    case "connect":
                        AskConnection();
                        break;
                    case "leave":
                        ServerManager.GetInstance().Remove(ServerManager.GetInstance()[ConsoleSplitted[1]]);
                        break;
                }
            }
        }

        private static void AskConnection()
        {
            Console.Write("Hostname: ");
            string Hostname = Console.ReadLine();
            int port = 0;
            do
            {
                Console.Write("Port: ");
            }
            while (!int.TryParse(Console.ReadLine(), out port));
            Console.Write("Nickname: ");
            string nickname = Console.ReadLine();
            Console.Write("QuitMessage: ");
            string QuitMessage = Console.ReadLine();
            Console.Write("InitialChannel: ");
            string channel = Console.ReadLine();
            toolbox.InstantiateConnection(Hostname, port, nickname, QuitMessage, channel);
        }

        private static void Init()
        {
            restart = false;

            try
            {
                DBProvider.Defragmentieren();
            }
            catch (Db4oIOException)
            {
                toolbox.Logging("Defragmentierung fehlgeschlagen, starte Workaround");
                DBProvider.Shutdown();
                File.Delete(DBProvider.DBPath);
                File.Move(DBProvider.DBPath + ".backup", DBProvider.DBPath);
                DBProvider.ReCreate();
                DBProvider.Defragmentieren();
            }

            BotSettings = new DBProvider().GetSimpleStorage("Bot");

            PluginManager.GetInstance().BeginInit(true);

            int count = new DBProvider().Query<User>().Count();
            toolbox.Logging(count + " Benutzer geladen!");

            ServerManager Servers = ServerManager.GetInstance();
            if (Servers.ConnectionCount == 0)
            {
                Console.WriteLine("Keine Verbindungen bekannt, starte Verbindungsassistent");
                AskConnection();
            }
            Servers.ConnectAll();

            toolbox.SafeThreadStart("ConsolenThread", true, HandleConsoleInput);
            AntiFloodingCount = 0;
            toolbox.SafeThreadStart("AntifloodingThread", true, AntiFlooding);
        }

        private static void Deinit()
        {
            PluginManager.Shutdown();
            DBProvider.Shutdown();
        }

        private static void Main()
        {
            Init();
            while (ServerManager.GetInstance().Connected)
            {
                Thread.Sleep(2000);
            }
            Deinit();
            if (restart == true)
            {
                Environment.Exit(99);
            }
            else
            {
                Environment.Exit(0);
            }
        }
    }
}