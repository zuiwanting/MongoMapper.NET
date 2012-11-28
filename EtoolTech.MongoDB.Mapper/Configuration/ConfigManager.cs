﻿namespace EtoolTech.MongoDB.Mapper.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    public class ConfigManager
    {
        #region Constants and Fields

        public static readonly MongoMapperConfiguration Config = MongoMapperConfiguration.GetConfig();

        private static readonly Dictionary<string, CollectionElement> ConfigByObject =
            new Dictionary<string, CollectionElement>();

        private static readonly Object LockObject = new Object();

        private static bool _setupLoaded;

        #endregion

        #region Public Properties

        public static TextWriter Out { get; set; }

        #endregion

        #region Properties

        internal static int? ActiveServers { get; set; }

        internal static bool IsReplicaSet { get; set; }

        #endregion

        #region Public Methods

        public static bool BalancedReading(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.BalancedReading;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Server.BalancedReading;
            }

            return Config.Server.BalancedReading;
        }

        public static string DataBaseName(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.Database;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Database.Name;
            }

            return Config.Database.Name;
        }

        public static bool EnableOriginalObject(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.EnableOriginalObject;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Context.EnableOriginalObject;
            }

            return Config.Context.EnableOriginalObject;
        }

        public static bool ExceptionOnDuplicateKey(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.ExceptionOnDuplicateKey;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Context.ExceptionOnDuplicateKey;
            }

            return Config.Context.ExceptionOnDuplicateKey;
        }

        public static bool Journal(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.Journal;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Context.Journal;
            }

            return Config.Context.Journal;
        }

        private static string _connectionString = String.Empty;

        public static void OverrideConnectionString(string connectionString)
        {
            _connectionString = connectionString;
        }

        public static string GetConnectionString(string objName)
        {
            if (!string.IsNullOrEmpty(_connectionString)) return _connectionString;
            
            string loginString = "";
            string userName = UserName(objName);

            if (!String.IsNullOrEmpty(userName))
            {
                loginString = String.Format("{0}:{1}@", userName, PassWord(objName));
            }

            string databaseName = DataBaseName(objName);

            string host = Host(objName);

            string hostsStrings = "";
            if (host.Contains(","))
            {
                string[] hostList = host.Split(',');
                hostsStrings = hostList.Aggregate(hostsStrings, (current, h) => current + (h + ","));
                if (!String.IsNullOrEmpty(hostsStrings))
                {
                    hostsStrings = hostsStrings.Remove(hostsStrings.Length - 1, 1);
                }
                IsReplicaSet = true;
            }
            else
            {
                hostsStrings = host;
            }

            string replicaOptions = "";

            string replicaSetName = ReplicaSetName(objName);
            if (!String.IsNullOrEmpty(replicaSetName))
            {
                replicaOptions = string.Format("replicaSet={0}", replicaSetName);
            }

            if (MinReplicaServersToWrite(objName) != 0)
            {
                replicaOptions += String.Format(";w={0}", MinReplicaServersToWrite(objName));
            }

            if (BalancedReading(objName))
            {
                replicaOptions += String.Format(";slaveOk=true");
            }

            if (replicaOptions.StartsWith(";"))
            {
                replicaOptions = replicaOptions.Remove(0, 1);
            }

            if (!String.IsNullOrEmpty(replicaOptions))
            {
                replicaOptions += ";";
            }

            //TODO: Fire and Forget solo se puede asignar si:
            //return _fsync != null || _journal != null || _w != null || _wTimeout != null;

            string connectionString =
                String.Format(
                    "mongodb://{4}{0}/{5}?{1}maxpoolsize={2};waitQueueTimeout={3}ms;journal={7}",
                    hostsStrings,
                    replicaOptions,
                    PoolSize(objName),
                    WaitQueueTimeout(objName) * 1000,
                    loginString,
                    databaseName,
                    (!SafeMode(objName)).ToString(CultureInfo.InvariantCulture).ToLower(),
                    Journal(objName).ToString(CultureInfo.InvariantCulture).ToLower());
            return connectionString;
        }

        public static string Host(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.Host;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Server.Host;
            }

            return Config.Server.Host;
        }

        public static int MaxDocumentSize(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.MaxDocumentSize;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Context.MaxDocumentSize;
            }

            return Config.Context.MaxDocumentSize;
        }

        public static int MinReplicaServersToWrite(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.MinReplicaServersToWrite;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Server.MinReplicaServersToWrite;
            }

            return Config.Server.MinReplicaServersToWrite;
        }

        public static string PassWord(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.PassWord;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Database.Password;
            }

            return Config.Database.Password;
        }

        public static int PoolSize(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.PoolSize;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Server.PoolSize;
            }

            return Config.Server.PoolSize;
        }

        public static string ReplicaSetName(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.ReplicaSetName;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Server.ReplicaSetName;
            }

            return Config.Server.ReplicaSetName;
        }

        public static bool SafeMode(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.SafeMode;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Context.SafeMode;
            }

            return Config.Context.SafeMode;
        }

        public static bool UseChildIncrementalId(string objName)
        {
            if (Helper.BufferIdIncrementables[objName] != null)
            {
                return Helper.BufferIdIncrementables[objName].ChildsIncremenalId;
            }

            if (CustomContext.Config != null)
            {
                return CustomContext.Config.UseChildIncrementalId;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Context.UseChidlsIncrementalId;
            }

            return Config.Context.UseChidlsIncrementalId;
        }

        public static bool UseIncrementalId(string objName)
        {
            if (Helper.BufferIdIncrementables[objName] != null)
            {
                return Helper.BufferIdIncrementables[objName].IncremenalId;
            }

            if (CustomContext.Config != null)
            {
                return CustomContext.Config.UseIncrementalId;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Context.UseIncrementalId;
            }

            return Config.Context.UseIncrementalId;
        }

        public static string UserName(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.UserName;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Database.User;
            }

            return Config.Database.User;
        }

        public static int WaitQueueTimeout(string objName)
        {
            if (CustomContext.Config != null)
            {
                return CustomContext.Config.WaitQueueTimeout;
            }

            CollectionElement cfg = FindByObjName(objName);

            if (cfg != null)
            {
                return cfg.Server.WaitQueueTimeout;
            }

            return Config.Server.WaitQueueTimeout;
        }

        #endregion

        #region Methods

        private static string CleanObjName(string objName)
        {
            if (objName.EndsWith("_Collection"))
            {
                objName = objName.Replace("_Collection", "");
            }
            return objName;
        }

        private static CollectionElement FindByObjName(string objName)
        {
            if (!_setupLoaded)
            {
                lock (LockObject)
                {
                    if (!_setupLoaded)
                    {
                        foreach (CollectionElement collection in Config.CollectionConfig)
                        {
                            ConfigByObject.Add(collection.Name, collection);
                        }
                        _setupLoaded = true;
                    }
                }
            }

            objName = CleanObjName(objName);

            return ConfigByObject.ContainsKey(objName) ? ConfigByObject[objName] : null;
        }

        #endregion
    }
}