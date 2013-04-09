﻿using Db4objects.Db4o;
using Db4objects.Db4o.Config;
using Db4objects.Db4o.Defragment;
using Db4objects.Db4o.Linq;
using FritzBot.DataModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FritzBot.Core
{
    public class DBProvider : IDisposable
    {
        private static IEmbeddedObjectContainer _db;
        private static IEmbeddedObjectContainer Datenbank
        {
            get
            {
                if (_db == null)
                {
                    _db = Db4oEmbedded.OpenFile(GetConfiguration(), DBPath);
                }
                return _db;
            }
        }

        private static string DBPath
        {
            get
            {
                return Path.Combine(Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath), "datenbank.db");
            }
        }

        private static IEmbeddedConfiguration GetConfiguration()
        {
            IEmbeddedConfiguration conf = Db4oEmbedded.NewConfiguration();
            conf.Common.ExceptionsOnNotStorable = true;
            return conf;
        }

        public static void Defragmentieren()
        {
            if (File.Exists(DBPath))
            {
                Shutdown();
                DefragmentConfig config = new DefragmentConfig(DBPath);
                config.Db4oConfig(GetConfiguration());
                Defragment.Defrag(config);
            }
        }

        public static void ReCreate()
        {
            List<object> allObjects;
            using (DBProvider db = new DBProvider())
            {
                allObjects = db.Query<object>().ToList();
            }
            Shutdown();
            File.Move(DBPath, DBPath + ".old");
            using (DBProvider db = new DBProvider())
            {
                foreach (object item in allObjects)
                {
                    db.SaveOrUpdate(item);
                }
            }
        }

        public static void Shutdown()
        {
            if (_db != null)
            {
                _db.Close();
                _db = null;
            }
        }

        public IQueryable<T> Query<T>()
        {
            return Datenbank.AsQueryable<T>();
        }

        public IQueryable<T> Query<T>(Func<T, bool> match)
        {
            return Datenbank.AsQueryable<T>().Where(match).AsQueryable();
        }

        public IQueryable<T> QueryLinkedData<T, L>(L instance)
            where T : LinkedData<L>
            where L : class
        {
            return Datenbank.AsQueryable<T>().Where(x => x.Reference == instance);
        }

        public User GetUser(string name)
        {
            return Datenbank.AsQueryable<User>().FirstOrDefault(x => x.Names.Contains(name));
        }

        public SimpleStorage GetSimpleStorage(string ID)
        {
            return GetSimpleStorage(null, ID);
        }

        public SimpleStorage GetSimpleStorage(object reference, string ID)
        {
            SimpleStorage storage = Query<SimpleStorage>(x => x.Reference == reference && x.ID == ID).FirstOrDefault();
            if (storage == null)
            {
                storage = new SimpleStorage();
                storage.ID = ID;
                storage.Reference = reference;
            }
            return storage;
        }

        public void SaveOrUpdate(object obj)
        {
            Datenbank.Store(obj);
        }

        public void Remove(object obj)
        {
            Datenbank.Delete(obj);
        }

        public void Dispose()
        {
            try
            {
                Datenbank.Commit();
            }
            catch
            {
                Datenbank.Rollback();
            }
        }
    }
}