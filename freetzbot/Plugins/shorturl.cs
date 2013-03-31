﻿using FritzBot.DataModel;

namespace FritzBot.Plugins
{
    [Module.Name("shorturl", "urlshort", "urlshortener")]
    [Module.Help("Kürzt den angegebenen Link zu einer tinyurl. Achte darauf, dass die URL ein gültiges http://adresse.tld Format hat. z.b. \"!shorturl http://google.de\"")]
    [Module.ParameterRequired]
    class shorturl : PluginBase, ICommand
    {
        public void Run(ircMessage theMessage)
        {
            theMessage.Answer(toolbox.ShortUrl(theMessage.CommandLine));
        }
    }
}