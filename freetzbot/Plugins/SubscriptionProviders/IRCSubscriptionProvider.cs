﻿using FritzBot.Core;
using FritzBot.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace FritzBot.Plugins.SubscriptionProviders
{
    [Module.Name("IRC")]
    [Module.Help("Stellt dir die Nachrichten über IRC zu, wahlweise über NOTICE oder PRIVMSG. !subscribe setup IRC NOTICE / PRIVMSG")]
    [Module.Hidden]
    public class IRCSubscriptionProvider : SubscriptionProvider
    {
        public override void SendNotification(User user, string message)
        {
            Irc UserConnection = ServerManager.GetInstance().GetAllConnections().FirstOrDefault(x => x.Channels.Any(y => y.User.Contains(user)));
            if (UserConnection != null)
            {
                SimpleStorage storage = GetSettings(new DBProvider(), user);
                if (storage.Get(PluginID, "PRIVMSG") == "PRIVMSG")
                {
                    UserConnection.Sendmsg(message, user.LastUsedName);
                }
                else
                {
                    UserConnection.Sendnotice(message, user.LastUsedName);
                }
            }
        }

        public override void ParseSubscriptionSetup(ircMessage theMessage)
        {
            if (theMessage.CommandArgs.Count < 3)
            {
                theMessage.Answer("Zu wenig Parameter, probier mal: !subscribe setup <SubscriptionProvider> <Einstellung>");
                return;
            }
            if (theMessage.CommandArgs[2] == "NOTICE" || theMessage.CommandArgs[2] == "PRIVMSG")
            {
                base.ParseSubscriptionSetup(theMessage);
            }
            else
            {
                theMessage.Answer("Ungültige Option, mögliche Optionen sind NOTICE oder PRIVMSG");
            }
        }
    }
}