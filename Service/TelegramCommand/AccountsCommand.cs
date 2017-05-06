﻿using System;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Common;
using PoGo.NecroBot.Logic.State;
using TinyIoC;
using PoGo.NecroBot.Logic.Model;

namespace PoGo.NecroBot.Logic.Service.TelegramCommand
{
    // TODO I18N
    public class AccountsCommand : CommandMessage
    {
        public override string Command => "/accounts";
        public override bool StopProcess => true;
        public override TranslationString DescriptionI18NKey => TranslationString.TelegramCommandAccountsDescription;
        public override TranslationString MsgHeadI18NKey => TranslationString.TelegramCommandAccountsMsgHead;

        public AccountsCommand(TelegramUtils telegramUtils) : base(telegramUtils)
        {
        }

        #pragma warning disable 1998 // added to get rid of compiler warning. Remove this if async code is used below.
        public override async Task<bool> OnCommand(ISession session, string cmd, Action<string> callback)
        #pragma warning restore 1998
        {
            string[] messagetext = cmd.Split(' ');

            string message = GetMsgHead(session, session.Profile.PlayerData.Username) + "\r\n\r\n";
            if (messagetext[0].ToLower() != Command)
            {
                return false;
            }

            var manager = TinyIoCContainer.Current.Resolve<MultiAccountManager>();
            if (manager.AllowMultipleBot())
            {
                using (var db = new AccountConfigContext())
                {
                    foreach (var item in db.Account)
                    {
                        message = message +
                                  $"{item.Username}({item.AuthType})     {item.Level}     {item.GetRuntime()}\r\n";
                    }
                }
            }
            else
            {
                message = message + "Multiple bots are disabled. please use /profile for current account details";
            }
            callback(message);
            return true;
        }
    }
}