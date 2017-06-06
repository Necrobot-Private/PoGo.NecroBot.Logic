﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Event.Snipe;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Tasks;
using POGOProtos.Enums;
using WebSocketSharp;
using Logger = PoGo.NecroBot.Logic.Logging.Logger;
using System.Runtime.Caching;
using System.Reflection;
using TinyIoC;
using static PoGo.NecroBot.Logic.Service.AnalyticsService;
using Pogo;
using PoGo.NecroBot.Logic.Utils;
using GeoCoordinatePortable;

namespace PoGo.NecroBot.Logic.Service
{
    public class BotDataSocketClient
    {
        public class SocketMessage
        {
            public string Header { get; set; }
            public string Body { get; set; }
            public long TimeTimestamp { get; set; }
            public string Hash { get; set; }
        }
        public class SocketClientUpdate
        {
            public List<EncounteredEvent> Pokemons { get; set; }

            public List<SnipeFailedEvent> SnipeFailedPokemons { get; set; }
            public List<string> ExpiredPokemons { get; set; }
            public string ClientVersion { get; set; }
            public List<string> ManualSnipes { get; set; }
            public string Identitier { get; set; }

            public SocketClientUpdate()
            {
                Identitier = TinyIoCContainer.Current.Resolve<ISession>().LogicSettings.DataSharingConfig.DataServiceIdentification;
                ManualSnipes = new List<string>();
                Pokemons = new List<EncounteredEvent>();
                SnipeFailedPokemons = new List<SnipeFailedEvent>();
                ExpiredPokemons = new List<string>();
                ClientVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            }

            public bool HasData()
            {
                return Pokemons.Count > 0 || SnipeFailedPokemons.Count > 0 || ExpiredPokemons.Count > 0;
            }
        }
        private static SocketClientUpdate clientData = new SocketClientUpdate();

        private const int POLLING_INTERVAL = 5000;

        public static void HandleEvent(AllBotSnipeEvent e, ISession session)
        {
            lock(clientData)
            {
                if(!string.IsNullOrEmpty(clientData.Identitier))
                {
                    clientData.ManualSnipes.Add(e.EncounterId);
                }
            }
        }
        public static void HandleEvent(IEvent evt, ISession session)
        {
        }

        public static void HandleEvent(SnipeFailedEvent e, ISession sesion)
        {
            lock (clientData)
            {
                clientData.SnipeFailedPokemons.Add(e);
            }
        }

        public static async Task HandleEvent(AnalyticsEvent e, ISession session)
        {
#pragma warning disable IDE0018 // Inline variable declaration - Build.Bat Error Happens if We Do
            Pokemon data = e.Data as Pokemon;
            ulong encounterId;
            var distance = LocationUtils.CalculateDistanceInMeters(new GeoCoordinate(session.Client.CurrentLatitude, session.Client.CurrentLongitude), new GeoCoordinate(data.Latitude, data.Longitude));
            var maxDistance = session.LogicSettings.EnableHumanWalkingSnipe ? (session.LogicSettings.HumanWalkingSnipeMaxDistance > 0 ? session.LogicSettings.HumanWalkingSnipeMaxDistance : 1500) : 10000;
            if (distance > maxDistance)
                return;
            
            switch (e.EventType)
            {
                case 1:
                    if (ulong.TryParse(data.EncounterId, out encounterId))
                    {
                        var encounteredEvent = new EncounteredEvent
                        {
                            PokemonId = (PokemonId)data.PokemonId,
                            Latitude = data.Latitude,
                            Longitude = data.Longitude,
                            IV = data.Iv,
                            Level = data.Level,
                            Expires = new DateTime(1970, 1, 1, 0, 0, 0).AddMilliseconds(data.ExpiredTime),
                            ExpireTimestamp = data.ExpiredTime,
                            SpawnPointId = data.SpawnPointId,
                            EncounterId = data.EncounterId,
                            Move1 = data.Move1,
                            Move2 = data.Move2,
                            IsRecievedFromSocket = true
                        };

                        session.EventDispatcher.Send(encounteredEvent);
                        if (session.LogicSettings.DataSharingConfig.AutoSnipe)
                        {
                            var move1 = PokemonMove.MoveUnset;
                            var move2 = PokemonMove.MoveUnset;
                            Enum.TryParse(encounteredEvent.Move1, true, out move1);
                            Enum.TryParse(encounteredEvent.Move2, true, out move2);

                            var added = await MSniperServiceTask.AddSnipeItem(session, new MSniperServiceTask.MSniperInfo2()
                            {
                                UniqueIdentifier = data.EncounterId,
                                Latitude = data.Latitude,
                                Longitude = data.Longitude,
                                EncounterId = encounterId,
                                SpawnPointId = data.SpawnPointId,
                                PokemonId = (short)data.PokemonId,
                                Level = data.Level,
                                Iv = data.Iv,
                                Move1 = move1,
                                Move2 = move2,
                                ExpiredTime = data.ExpiredTime
                            }).ConfigureAwait(false);

                            if (added)
                            {
                                session.EventDispatcher.Send(new AutoSnipePokemonAddedEvent(encounteredEvent));
                            }
                        }
                    }
                    break;

                case 2:
                    MSniperServiceTask.RemoveExpiredSnipeData(session, data.EncounterId);
                    break;
            }
#pragma warning restore IDE0018 // Inline variable declaration - Build.Bat Error Happens if We Do
        }

        public static void Listen(IEvent evt, ISession session)
        {
            dynamic eve = evt;

            try
            {
                HandleEvent(eve, session);
            }
            catch
            {
            }
        }

        private static void HandleEvent(EncounteredEvent eve, ISession session)
        {
            lock (clientData)
            {
                if (eve.IsRecievedFromSocket || cache.Get(eve.EncounterId) != null) return;
                clientData.Pokemons.Add(eve);
            }
        }
        //private static SnipePokemonUpdateEvent lastEncouteredEvent;
        private static void HandleEvent(SnipePokemonUpdateEvent eve, ISession session)
        {
            lock(clientData)
            {
                clientData.ExpiredPokemons.Add(eve.EncounterId);
            }
        }
        private static string Serialize(dynamic evt)
        {
            var jsonSerializerSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };

            // Add custom seriaizer to convert uong to string (ulong shoud not appear to json according to json specs)
            jsonSerializerSettings.Converters.Add(new IdToStringConverter());

            string json = JsonConvert.SerializeObject(evt, Formatting.None, jsonSerializerSettings);
            //json = Regex.Replace(json, @"\\\\|\\(""|')|(""|')", match => {
            //    if (match.Groups[1].Value == "\"") return "\""; // Unescape \"
            //    if (match.Groups[2].Value == "\"") return "'";  // Replace " with '
            //    if (match.Groups[2].Value == "'") return "\\'"; // Escape '
            //    return match.Value;                             // Leave \\ and \' unchanged
            //});
            return json;
        }

        static List<EncounteredEvent> processing = new List<EncounteredEvent>();

        public static String SHA256Hash(String value)
        {
            StringBuilder Sb = new StringBuilder();
            using (SHA256 hash = SHA256.Create())
            {
                Encoding enc = Encoding.UTF8;
                Byte[] result = hash.ComputeHash(enc.GetBytes(value));

                foreach (Byte b in result)
                    Sb.Append(b.ToString("x2"));
            }

            return Sb.ToString();
        }

        public static async Task Start(Session session, string encryptKey, CancellationToken cancellationToken)
        {

            //Disable autosniper service until finger out how to make it work with API change

            await Task.Delay(30000, cancellationToken); //delay running 30s

            ServicePointManager.Expect100Continue = false;

            cancellationToken.ThrowIfCancellationRequested();

            while (true && !termintated)
            {
                var socketURL = servers.Dequeue();
                // Logger.Write($"Connecting to {socketURL} ....");
                await ConnectToServer(session, socketURL, encryptKey);
                servers.Enqueue(socketURL);
            }

        }
        public static async Task ConnectToServer(ISession session, string socketURL, string encryptKey)
        {
            if (!string.IsNullOrEmpty(session.LogicSettings.DataSharingConfig.SnipeDataAccessKey))
            {
                socketURL += "&access_key=" + session.LogicSettings.DataSharingConfig.SnipeDataAccessKey;
            }

            int retries = 0;
            using (var ws = new WebSocket(socketURL))
            {
                ws.Log.Level = LogLevel.Fatal; ;
                ws.Log.Output = (logData, message) =>
                {
                    //silenly, no log exception message to screen that scare people :)
                };

                ws.OnMessage += (sender, e) => { OnSocketMessageRecieved(session, sender, e); };

                ws.Connect();
                while (true && !termintated)
                {
                    try
                    {
                        if (retries == 3)
                        {
                            //failed to make connection to server  times continuing, temporary stop for 10 mins.
                            /*
                            session.EventDispatcher.Send(new WarnEvent()
                            {
                                Message = $"Couldn't establish the connection to necrobot socket server : {socketURL}"
                            });
                            */
                            if (session.LogicSettings.DataSharingConfig.EnableFailoverDataServers && servers.Count > 1)
                            {
                                break;
                            }
                            await Task.Delay(1 * 60 * 1000);
                            retries = 0;
                        }

                        if (ws.ReadyState != WebSocketState.Open)
                        {
                            retries++;
                            ws.Connect();
                        }

                        while (ws.ReadyState == WebSocketState.Open && !termintated)
                        {
                            //Logger.Write("Connected to necrobot data service.");
                            retries = 0;

                            if (ws.IsAlive && clientData.HasData())
                            {
                                var data = JsonConvert.SerializeObject(clientData);// Serialize(processing);
                                clientData = new SocketClientUpdate();

                                var message = Encrypt(data, encryptKey);
                                var actualMessage = JsonConvert.SerializeObject(message);
                                ws.Send($"42[\"client-update\",{actualMessage}]");
                            }
                            else
                            {
                                var pingMessage = JsonConvert.SerializeObject(new { Ping = DateTime.Now });
                                ws.Send($"42[\"ping-server\",{pingMessage}");
                            }
                            await Task.Delay(POLLING_INTERVAL);
                        }
                    }
                    catch (IOException)
                    {
                        /*
                        session.EventDispatcher.Send(new WarnEvent
                        {
                            Message = "Disconnected from necrobot socket. New connection will be established when service becomes available..."
                        });
                        */
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        //everytime disconnected with server bot wil reconnect after 15 sec
                        await Task.Delay(POLLING_INTERVAL);
                    }
                }
            }
        }

        private static void OnSocketMessageRecieved(ISession session, object sender, MessageEventArgs e)
        {
            try
            {
                OnPokemonRemoved(session, e.Data);
                OnPokemonUpdateData(session, e.Data);
                OnPokemonData(session, e.Data);
                OnSnipePokemon(session, e.Data);
                OnServerMessage(session, e.Data);
            }
            catch (Exception ex)
            {
                Logger.Debug("ERROR TO ADD SNIPE< DEBUG ONLY " + ex.Message + "\r\n " + ex.StackTrace);
            }
        }

        private static DateTime lastWarningMessage = DateTime.MinValue;
        private static bool termintated = false;
        private static void OnServerMessage(ISession session, string message)
        {
            var match = Regex.Match(message, "42\\[\"server-message\",(.*)]");
            if (match != null && !string.IsNullOrEmpty(match.Groups[1].Value))
            {
                var messag = match.Groups[1].Value;
                if (message.Contains("The connection has been denied") && lastWarningMessage > DateTime.Now.AddMinutes(-5)) return;
                lastWarningMessage = DateTime.Now;
                session.EventDispatcher.Send(new NoticeEvent()
                {
                    Message = "(SNIPE SERVER) " + match.Groups[1].Value
                });
            }
        }

        private static void ONFPMBridgeData(ISession session, string message)
        {
            var match = Regex.Match(message, "42\\[\"fpm\",(.*)]");
            if (match != null && !string.IsNullOrEmpty(match.Groups[1].Value))
            {
                //var data = JsonConvert.DeserializeObject<List<Logic.Tasks.HumanWalkSnipeTask.FastPokemapItem>>(match.Groups[1].Value);

                // jjskuld - Ignore CS4014 warning for now.
#pragma warning disable 4014
                HumanWalkSnipeTask.AddFastPokemapItem(match.Groups[1].Value);
#pragma warning restore 4014
            }
        }

        public static bool CheckIfPokemonBeenCaught(double lat, double lng, PokemonId id, ulong encounterId,
            ISession session)
        {
            if (session.Cache.Get(CatchPokemonTask.GetUsernameGeoLocationCacheKey(session.Settings.Username, id, lat, lng)) != null) return true;
            if (encounterId > 0 && session.Cache[CatchPokemonTask.GetEncounterCacheKey(encounterId)] != null) return true;

            return false;
        }

        private static void OnPokemonUpdateData(ISession session, string message)
        {
            var match = Regex.Match(message, "42\\[\"pokemon-update\",(.*)]");
            if (match != null && !string.IsNullOrEmpty(match.Groups[1].Value))
            {
                var data = JsonConvert.DeserializeObject<EncounteredEvent>(match.Groups[1].Value);
                MSniperServiceTask.RemoveExpiredSnipeData(session, data.EncounterId);
            }
        }

        private static void OnPokemonRemoved(ISession session, string message)
        {
            var match = Regex.Match(message, "42\\[\"pokemon-remove\",(.*)]");
            if (match != null && !string.IsNullOrEmpty(match.Groups[1].Value))
            {
                var data = JsonConvert.DeserializeObject<EncounteredEvent>(match.Groups[1].Value);
                MSniperServiceTask.RemoveExpiredSnipeData(session, data.EncounterId);
            }
        }

        private static MemoryCache cache = new MemoryCache("dump");

        //static int count = 0;
        private static void OnPokemonData(ISession session, string message)
        {
#pragma warning disable IDE0018 // Inline variable declaration - Build.Bat Error Happens if We Do
            ulong encounterid;
            var match = Regex.Match(message, "42\\[\"pokemon\",(.*)]");
            if (match != null && !string.IsNullOrEmpty(match.Groups[1].Value))
            {
                var data = JsonConvert.DeserializeObject<EncounteredEvent>(match.Groups[1].Value);
                data.IsRecievedFromSocket = true;
                if (Math.Abs(data.Latitude) > 90 || Math.Abs(data.Longitude) > 180)
                {
                    return;
                };

                var distance = LocationUtils.CalculateDistanceInMeters(new GeoCoordinate(session.Client.CurrentLatitude, session.Client.CurrentLongitude), new GeoCoordinate(data.Latitude, data.Longitude));
                var maxDistance = session.LogicSettings.EnableHumanWalkingSnipe ? (session.LogicSettings.HumanWalkingSnipeMaxDistance > 0 ? session.LogicSettings.HumanWalkingSnipeMaxDistance : 1500) : 10000;
                if (distance > maxDistance)
                    return;

                ulong.TryParse(data.EncounterId, out encounterid);
                if (encounterid > 0 && cache.Get(encounterid.ToString()) == null)
                {
                    cache.Add(encounterid.ToString(), DateTime.Now, DateTime.Now.AddMinutes(15));
                }

                session.EventDispatcher.Send(data);
                if (session.LogicSettings.DataSharingConfig.AutoSnipe)
                {
                    var move1 = PokemonMove.MoveUnset;
                    var move2 = PokemonMove.MoveUnset;
                    Enum.TryParse(data.Move1, true, out move1);
                    Enum.TryParse(data.Move2, true, out move2);

                    bool caught = CheckIfPokemonBeenCaught(data.Latitude, data.Longitude,
                        data.PokemonId, encounterid, session);
                    if (!caught)
                    {
                        var added = MSniperServiceTask.AddSnipeItem(session, new MSniperServiceTask.MSniperInfo2()
                        {
                            UniqueIdentifier = data.EncounterId,
                            Latitude = data.Latitude,
                            Longitude = data.Longitude,
                            EncounterId = encounterid,
                            SpawnPointId = data.SpawnPointId,
                            PokemonId = (short)data.PokemonId,
                            Level = data.Level,
                            Iv = data.IV,
                            Move1 = move1,
                            Move2 = move2,
                            ExpiredTime = data.ExpireTimestamp
                        }).Result;
                        if (added)
                        {
                            session.EventDispatcher.Send(new AutoSnipePokemonAddedEvent(data));
                        }
                    }
                }
            }
#pragma warning restore IDE0018 // Inline variable declaration - Build.Bat Error Happens if We Do
        }

        private static void OnSnipePokemon(ISession session, string message)
        {
#pragma warning disable IDE0018 // Inline variable declaration - Build.Bat Error Happens if We Do
            ulong encounterid;
            var match = Regex.Match(message, "42\\[\"snipe-pokemon\",(.*)]");
            if (match != null && !string.IsNullOrEmpty(match.Value) && !string.IsNullOrEmpty(match.Groups[1].Value))
            {
                var data = JsonConvert.DeserializeObject<EncounteredEvent>(match.Groups[1].Value);

                //not your snipe item, return need more encrypt here and configuration to allow catch others item
                if (string.IsNullOrEmpty(session.LogicSettings.DataSharingConfig.DataServiceIdentification) ||
                    string.IsNullOrEmpty(data.RecieverId) ||
                    data.RecieverId.ToLower() != session.LogicSettings.DataSharingConfig.DataServiceIdentification.ToLower()) return;

                var move1 = PokemonMove.Absorb;
                var move2 = PokemonMove.Absorb;
                Enum.TryParse(data.Move1, true, out move1);
                Enum.TryParse(data.Move1, true, out move2);
                ulong.TryParse(data.EncounterId, out encounterid);

                bool caught = CheckIfPokemonBeenCaught(data.Latitude, data.Longitude, data.PokemonId, encounterid,
                    session);
                if (caught)
                {
                    Logger.Write("[SNIPE IGNORED] - Your snipe pokemon has already been caught by bot",
                        Logic.Logging.LogLevel.Sniper);
                    return;
                }

                MSniperServiceTask.AddSnipeItem(session, new MSniperServiceTask.MSniperInfo2()
                {
                    UniqueIdentifier = data.EncounterId,
                    Latitude = data.Latitude,
                    Longitude = data.Longitude,
                    EncounterId = encounterid,
                    SpawnPointId = data.SpawnPointId,
                    Level = data.Level,
                    PokemonId = (short)data.PokemonId,
                    Iv = data.IV,
                    Move1 = move1,
                    ExpiredTime = data.ExpireTimestamp,
                    Move2 = move2
                }, true).Wait();
            }
#pragma warning restore IDE0018 // Inline variable declaration - Build.Bat Error Happens if We Do
        }
        private static Queue<string> servers = new Queue<string>();
        public static Task StartAsync(Session session, string encryptKey,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var config = session.LogicSettings.DataSharingConfig;

            if (config.EnableSyncData)
            {
                servers.Enqueue(config.DataRecieverURL);

                if (config.EnableFailoverDataServers)
                {
                    foreach (var item in config.FailoverDataServers.Split(";".ToCharArray(), StringSplitOptions.RemoveEmptyEntries))
                    {
                        servers.Enqueue(item);
                    }
                }
            }

            return Task.Run(() => Start(session, encryptKey, cancellationToken), cancellationToken);
        }

        public static SocketMessage Encrypt(string message, string encryptKey)
        {
            var encryptedtulp = Encrypt(message, encryptKey, false);

            var socketMessage = new SocketMessage()
            {
                TimeTimestamp = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds,
                Header = encryptedtulp.Item1,
                Body = encryptedtulp.Item2
            };
            socketMessage.Hash = CalculateMD5Hash($"{socketMessage.TimeTimestamp}{socketMessage.Body}{socketMessage.Header}");

            return socketMessage;
        }
        public static string CalculateMD5Hash(string input)
        {
            // step 1, calculate MD5 hash from input

            MD5 md5 = MD5.Create();

            byte[] inputBytes = Encoding.ASCII.GetBytes(input);

            byte[] hash = md5.ComputeHash(inputBytes);

            // step 2, convert byte array to hex string

            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < hash.Length; i++)

            {

                sb.Append(hash[i].ToString("X2"));

            }

            return sb.ToString();

        }


        public static Tuple<string, string> Encrypt(string toEncrypt, string key, bool useHashing)
        {
            byte[] keyArray;
            byte[] toEncryptArray = Encoding.UTF8.GetBytes(toEncrypt);

            if (useHashing)
            {
                MD5CryptoServiceProvider hashmd5 = new MD5CryptoServiceProvider();
                keyArray = hashmd5.ComputeHash(Encoding.UTF8.GetBytes(key));
            }
            else
                keyArray = Encoding.UTF8.GetBytes(key);

            var tdes = new TripleDESCryptoServiceProvider()
            {
                Key = keyArray
            };
            // tdes.Mode = CipherMode.CBC;  // which is default     
            // tdes.Padding = PaddingMode.PKCS7;  // which is default

            //Console.WriteLine("iv: {0}", Convert.ToBase64String(tdes.IV));

            ICryptoTransform cTransform = tdes.CreateEncryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0,
                toEncryptArray.Length);
            string iv = Convert.ToBase64String(tdes.IV);
            string message = Convert.ToBase64String(resultArray, 0, resultArray.Length);
            return new Tuple<string, string>(iv, message);
        }

    }
}