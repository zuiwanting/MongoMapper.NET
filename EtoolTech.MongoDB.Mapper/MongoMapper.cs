﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml.Serialization;
using EtoolTech.MongoDB.Mapper.Configuration;
using EtoolTech.MongoDB.Mapper.Core;
using EtoolTech.MongoDB.Mapper.Exceptions;
using EtoolTech.MongoDB.Mapper.Interfaces;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using MongoDB.Driver.Linq;

namespace EtoolTech.MongoDB.Mapper
{
    [Serializable]
    public abstract class MongoMapper : IBsonSerializable, IMongoMapperOriginable, IMongoMapperRelationable,
                                        IMongoMapperWriteable, IMongoMapperIdeable
    {
        #region Eventos

        #region Delegates

        public delegate void OnAfterCompleteEventHandler(object sender, EventArgs e);

        public delegate void OnAfterDeleteEventHandler(object sender, EventArgs e);

        public delegate void OnAfterInsertEventHandler(object sender, EventArgs e);

        public delegate void OnAfterModifyEventHandler(object sender, EventArgs e);

        public delegate void OnBeforeDeleteEventHandler(object sender, EventArgs e);

        public delegate void OnBeforeInsertEventHandler(object sender, EventArgs e);

        public delegate void OnBeforeModifyEventHandler(object sender, EventArgs e);

        #endregion

        public event OnBeforeInsertEventHandler OnBeforeInsert;

        public event OnAfterInsertEventHandler OnAfterInsert;

        public event OnBeforeModifyEventHandler OnBeforeModify;

        public event OnAfterModifyEventHandler OnAfterModify;

        public event OnBeforeDeleteEventHandler OnBeforeDelete;

        public event OnAfterDeleteEventHandler OnAfterDelete;

        public event OnAfterCompleteEventHandler OnAfterComplete;

        #endregion

        private static readonly IFinder Finder = new Finder();

        private static readonly IRelations Relations = new Relations();

        private static readonly IEvents Events = new Events();

        private readonly Type _classType;
        private readonly Dictionary<string, object> _relationBuffer = new Dictionary<string, object>();
        [BsonIgnore] internal bool RepairCollection;

        private string _stringOriginalObject;
        private object _tOriginalObjet;

        protected MongoMapper()
        {
            _classType = GetType();
            Helper.RebuildClass(_classType, RepairCollection);
        }

        #region IBsonSerializable Members

        public object Deserialize(BsonReader bsonReader, Type nominalType, IBsonSerializationOptions options)
        {
            object o = BsonClassMapSerializer.Instance.Deserialize(bsonReader, nominalType, options);
            //Guardamos el obj original en JSV para romper la referencia
            if (ConfigManager.EnableOriginalObject)
            {
                ((IMongoMapperStringOriginalObject) o).StringOriginalObject = Serializer.Serialize(o);
            }
            return o;
        }

        public bool GetDocumentId(out object id, out Type idNominalType, out IIdGenerator idGenerator)
        {
            return BsonClassMapSerializer.Instance.GetDocumentId(this, out id, out idNominalType, out idGenerator);
        }

        public void Serialize(BsonWriter bsonWriter, Type nominalType, IBsonSerializationOptions options)
        {
            BsonClassMapSerializer.Instance.Serialize(bsonWriter, nominalType, this, options);
        }

        public void SetDocumentId(object id)
        {
            BsonClassMapSerializer.Instance.SetDocumentId(this, id);
        }

        #endregion

        #region IMongoMapperIdeable Members

        [BsonId(IdGenerator = typeof (MongoMapperIdGenerator))]
        [XmlIgnore]
        public long MongoMapper_Id { get; set; }

        #endregion

        #region IMongoMapperOriginable Members

        [BsonIgnore]
        public string StringOriginalObject
        {
            get { return _stringOriginalObject; }
            set
            {
                _stringOriginalObject = value;
                _tOriginalObjet = null;
            }
        }

        #endregion

        //TODO: Pendiente temas de transacciones
        //public bool Commited;
        //public CommitOperation CommitOp;
        //public string TransactionID;

        #region IMongoMapperRelationable Members

        public List<T> GetRelation<T>(string relation)
        {
            if (!_relationBuffer.ContainsKey(relation))
            {
                _relationBuffer.Add(relation, Relations.GetRelation<T>(this, relation, _classType, Finder));
            }
            return (List<T>) _relationBuffer[relation];
        }

        public void EnsureUpRelations()
        {
            Relations.EnsureUpRelations(this, _classType, Finder);
        }

        public void EnsureDownRelations()
        {
            Relations.EnsureDownRelations(this, _classType, Finder);
        }

        #endregion

        public static IQueryable<T> QueryContext<T>()
        {
            return CollectionsManager.GetCollection<T>(typeof (T).Name).AsQueryable<T>();
        }

        private Dictionary<string, object> GetKeyValues()
        {
            var result = new Dictionary<string, object>();
            foreach (string keyField in Helper.GetPrimaryKey(_classType))
            {
                PropertyInfo propertyInfo = _classType.GetProperty(keyField);
                result.Add(keyField, propertyInfo.GetValue(this, null));
            }
            return result;
        }

        #region Objeto Original para comprobar cambios

        public T GetOriginalObject<T>()
        {
            if (!ConfigManager.EnableOriginalObject)
            {
                throw new NotImplementedException("This functionality is disabled, enable it in the App.config");
            }

            if (MongoMapper_Id == default(long) || string.IsNullOrEmpty(StringOriginalObject))
            {
                return (Activator.CreateInstance<T>());
            }
            return GetOriginalT<T>();
        }

        public T GetOriginalT<T>()
        {
            if (_tOriginalObjet != null)
            {
                return (T) _tOriginalObjet;
            }

            _tOriginalObjet = Serializer.Deserialize<T>(StringOriginalObject);
            return (T) _tOriginalObjet;
        }

        #endregion

        #region Write Methods

        public void Save<T>()
        {
            PropertyValidator.Validate(this, _classType);

            if (MongoMapper_Id == default(long))
            {
                long id = Finder.FindIdByKey<T>(GetKeyValues());
                if (id == default(long))
                {
                    InsertDocument();
                }
                else
                {
                    //Si llega aqui ya existe un registro con esa key y no es el que tenemos cargado
                    if (ConfigManager.ExceptionOnDuplicateKey)
                    {
                        throw new DuplicateKeyException();
                    }

                    UpdateDocument(id);
                }

                //Si salvan y no se pide el objeto otra vez la string json queda vacia, la llenamos aqui
                //TODO: No estoy muy convencido de esto revisar ...                
                if (string.IsNullOrEmpty(StringOriginalObject))
                {
                    StringOriginalObject = Serializer.Serialize(this);
                }
            }
            else
            {
                UpdateDocument(MongoMapper_Id);
            }
        }

        public void UpdateDocument(long id)
        {
            Events.BeforeUpdateDocument(this, OnBeforeModify, _classType);

            EnsureUpRelations();

            MongoMapper_Id = id;

            SafeModeResult result =
                CollectionsManager.GetCollection(CollectionsManager.GetCollectioName(_classType.Name)).Save(
                    this, SafeMode.Create(ConfigManager.SafeMode, ConfigManager.FSync));

            Events.AfterUpdateDocument(this, OnAfterModify, OnAfterComplete, _classType);
        }

        public void InsertDocument()
        {
            Events.BeforeInsertDocument(this, OnBeforeInsert, _classType);

            EnsureUpRelations();

            SafeModeResult result =
                CollectionsManager.GetCollection(CollectionsManager.GetCollectioName(_classType.Name)).Insert(
                    this, SafeMode.Create(ConfigManager.SafeMode, ConfigManager.FSync));

            Events.AfterInsertDocument(this, OnAfterInsert, OnAfterComplete, _classType);
        }

        public void Delete<T>()
        {
            Events.BeforeDeleteDocument(this, OnBeforeDelete, _classType);

            EnsureDownRelations();

            DeleteDocument<T>();

            Events.AfterDeleteDocument(this, OnAfterDelete, OnAfterComplete, _classType);
        }

        public void DeleteDocument<T>()
        {
            if (MongoMapper_Id == default(long))
            {
                MongoMapper_Id = Finder.FindIdByKey<T>(GetKeyValues());
            }
            QueryComplete query = Query.EQ("_id", MongoMapper_Id);
            FindAndModifyResult result =
                CollectionsManager.GetCollection(CollectionsManager.GetCollectioName(_classType.Name)).FindAndRemove(
                    query, null);
        }

        #endregion

        #region FindAsList/Cursor Methods

        public static T FindByKey<T>(params object[] values)
        {
            return Finder.FindByKey<T>(values);
        }

        public static List<T> FindAsList<T>(QueryComplete query)
        {
            return Finder.FindAsList<T>(query);
        }

        public static MongoCursor<T> FindAsCursor<T>(QueryComplete query = null)
        {
            return Finder.FindAsCursor<T>(query);
        }

        public static List<T> FindAsList<T>(string fieldName, object value)
        {
            return Finder.FindAsList<T>(MongoQuery.Eq(fieldName, value));
        }

        public static MongoCursor<T> FindAsCursor<T>(string fieldName, object value)
        {
            return Finder.FindAsCursor<T>(MongoQuery.Eq(fieldName, value));
        }

        public static List<T> FindAsList<T>(Expression<Func<T, object>> exp)
        {
            return Finder.FindAsList(exp);
        }

        public static MongoCursor<T> FindAsCursor<T>(Expression<Func<T, object>> exp)
        {
            return Finder.FindAsCursor(exp);
        }

        public static List<T> AllAsList<T>()
        {
            return Finder.AllAsList<T>();
        }

        public static MongoCursor<T> AllAsCursor<T>()
        {
            return Finder.AllAsCursor<T>();
        }

        #endregion
    }
}