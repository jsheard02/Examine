﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Web;
using System.Xml.Linq;
using Examine;
using Examine.Config;
using Examine.Providers;
using Lucene.Net.Index;
using umbraco.cms.businesslogic;
using UmbracoExamine.DataServices;
using Examine.LuceneEngine;
using Examine.LuceneEngine.Config;
using Examine.LuceneEngine.Indexing;
using UmbracoExamine.Config;
using Examine.LuceneEngine.Providers;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using umbraco.BasePages;
using Directory = Lucene.Net.Store.Directory;


namespace UmbracoExamine
{
    /// <summary>
    /// 
    /// </summary>
    internal class UmbracoContentIndexer : BaseUmbracoIndexer
    {
        #region Constructors

        /// <summary>
        /// Default constructor
        /// </summary>
        public UmbracoContentIndexer()
            : base() { }

        /// <summary>
        /// Constructor to allow for creating an indexer at runtime
        /// </summary>
        /// <param name="indexerData"></param>
        /// <param name="indexPath"></param>
        /// <param name="dataService"></param>
        /// <param name="analyzer"></param>
		
		public UmbracoContentIndexer(IIndexCriteria indexerData, DirectoryInfo indexPath, IDataService dataService, Analyzer analyzer, bool async)
            : base(indexerData, indexPath, dataService, analyzer, async) { }

		/// <summary>
		/// Constructor to allow for creating an indexer at runtime
		/// </summary>
		/// <param name="indexerData"></param>
		/// <param name="luceneDirectory"></param>
		/// <param name="dataService"></param>
		/// <param name="analyzer"></param>
		/// <param name="async"></param>
		
		public UmbracoContentIndexer(IIndexCriteria indexerData, Lucene.Net.Store.Directory luceneDirectory, IDataService dataService, Analyzer analyzer, bool async)
			: base(indexerData, luceneDirectory, dataService, analyzer, async) { }

        
        public UmbracoContentIndexer(IIndexCriteria indexerData, IndexWriter writer, IDataService dataService, bool async)
            : base(indexerData, writer, dataService, async) { }

        #endregion

        #region Constants & Fields

        /// <summary>
        /// Used to store the path of a content object
        /// </summary>
        public const string IndexPathFieldName = "__Path";
        public const string NodeTypeAliasFieldName = "__NodeTypeAlias";

        /// <summary>
        /// A type that defines the type of index for each Umbraco field (non user defined fields)
        /// Alot of standard umbraco fields shouldn't be tokenized or even indexed, just stored into lucene
        /// for retreival after searching.
        /// </summary>
        internal static readonly Dictionary<string, string> IndexFieldPolicies
            = new Dictionary<string, string>()
            {
                { "id", FieldDefinitionTypes.Raw},
                { "version", FieldDefinitionTypes.Raw},
                { "parentID", FieldDefinitionTypes.Raw},
                { "level", FieldDefinitionTypes.Raw},
                { "writerID", FieldDefinitionTypes.Raw},
                { "creatorID", FieldDefinitionTypes.Raw},
                { "nodeType", FieldDefinitionTypes.Raw},
                { "template", FieldDefinitionTypes.Raw},
                { "sortOrder", FieldDefinitionTypes.Raw},
                { "createDate", FieldDefinitionTypes.Raw},
                { "updateDate", FieldDefinitionTypes.Raw},
                { "nodeName", FieldDefinitionTypes.FullText},
                { "urlName", FieldDefinitionTypes.Raw},
                { "writerName", FieldDefinitionTypes.Raw},
                { "creatorName", FieldDefinitionTypes.Raw},
                { "nodeTypeAlias", FieldDefinitionTypes.FullText},
                { "path", FieldDefinitionTypes.Raw}
            };

        #endregion

        #region Initialize

        /// <summary>
        /// Set up all properties for the indexer based on configuration information specified. This will ensure that
        /// all of the folders required by the indexer are created and exist. This will also create an instruction
        /// file declaring the computer name that is part taking in the indexing. This file will then be used to
        /// determine the master indexer machine in a load balanced environment (if one exists).
        /// </summary>
        /// <param name="name">The friendly name of the provider.</param>
        /// <param name="config">A collection of the name/value pairs representing the provider-specific attributes specified in the configuration for this provider.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// The name of the provider is null.
        /// </exception>
        /// <exception cref="T:System.ArgumentException">
        /// The name of the provider has a length of zero.
        /// </exception>
        /// <exception cref="T:System.InvalidOperationException">
        /// An attempt is made to call <see cref="M:System.Configuration.Provider.ProviderBase.Initialize(System.String,System.Collections.Specialized.NameValueCollection)"/> on a provider after the provider has already been initialized.
        /// </exception>
        
        public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
        {

            //check if there's a flag specifying to support unpublished content,
            //if not, set to false;
            bool supportUnpublished;
            if (config["supportUnpublished"] != null && bool.TryParse(config["supportUnpublished"], out supportUnpublished))
                SupportUnpublishedContent = supportUnpublished;
            else
                SupportUnpublishedContent = false;


            //check if there's a flag specifying to support protected content,
            //if not, set to false;
            bool supportProtected;
            if (config["supportProtected"] != null && bool.TryParse(config["supportProtected"], out supportProtected))
                SupportProtectedContent = supportProtected;
            else
                SupportProtectedContent = false;


            base.Initialize(name, config);
        }

        #endregion

        #region Properties

        /// <summary>
        /// By default this is false, if set to true then the indexer will include indexing content that is flagged as publicly protected.
        /// This property is ignored if SupportUnpublishedContent is set to true.
        /// </summary>
        public bool SupportProtectedContent { get; protected internal set; }

        protected override IEnumerable<string> SupportedTypes
        {
            get
            {
                return new string[] { IndexTypes.Content, IndexTypes.Media };
            }
        }

        #endregion

        #region Event handlers

        protected override void OnIndexingError(IndexingErrorEventArgs e)
        {
            DataService.LogService.AddErrorLog(e.NodeId, string.Format("{0},{1}, IndexSet: {2}", e.Message, e.InnerException != null ? e.InnerException.Message : "", this.IndexSetName));
            base.OnIndexingError(e);
        }

        //protected override void OnDocumentWriting(DocumentWritingEventArgs docArgs)
        //{
        //    DataService.LogService.AddVerboseLog(docArgs.NodeId, string.Format("({0}) DocumentWriting event for node ({1})", this.Name, LuceneIndexFolder.FullName));
        //    base.OnDocumentWriting(docArgs);
        //}
        
        protected override void OnIndexOptimizing(EventArgs e)
        {
            DataService.LogService.AddInfoLog(-1, string.Format("Index is being optimized"));
            base.OnIndexOptimizing(e);
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Deletes a node from the index.                
        /// </summary>
        /// <remarks>
        /// When a content node is deleted, we also need to delete it's children from the index so we need to perform a 
        /// custom Lucene search to find all decendents and create Delete item queues for them too.
        /// </remarks>
        /// <param name="nodeId">ID of the node to delete</param>
        public override void DeleteFromIndex(string nodeId)
        {
            //find all descendants based on path
            var descendantPath = string.Format(@"\-1\,*{0}\,*", nodeId);
            var rawQuery = string.Format("{0}:{1}", IndexPathFieldName, descendantPath);
            var c = GetSearcher().CreateSearchCriteria();
            var filtered = c.RawQuery(rawQuery);
            var results = GetSearcher().Search(filtered);

            DataService.LogService.AddVerboseLog(int.Parse(nodeId), string.Format("DeleteFromIndex with query: {0} (found {1} results)", rawQuery, results.Count()));

            //need to create a delete queue item for each one found
            foreach (var r in results)
            {
                EnqueueIndexOperation(new IndexOperation(IndexItem.ForId(r.Id.ToString()),IndexOperationType.Delete));
                //SaveDeleteIndexQueueItem(new KeyValuePair<string, string>(IndexNodeIdFieldName, r.Id.ToString()));
            }

            base.DeleteFromIndex(nodeId);
        }
        #endregion

        #region Protected
        
        public override void RebuildIndex()
        {
            DataService.LogService.AddVerboseLog(-1, "Rebuilding index");
            base.RebuildIndex();
        }

        /// <summary>
        /// Override this method to strip all html from all user fields before raising the event, then after the event 
        /// ensure our special Path field is added to the collection
        /// </summary>
        /// <param name="e"></param>
        protected override void OnGatheringNodeData(IndexingItemEventArgs e)
        {
            //TODO: This needs to be done with an value indexer
            ////strip html of all users fields
            //// Get all user data that we want to index and store into a dictionary 
            //foreach (var field in IndexerData.UserFields)
            //{
            //    if (e.Fields.ContainsKey(field.Name))
            //    {
            //        e.Fields[field.Name] = DataService.ContentService.StripHtml(e.Fields[field.Name]);
            //    }
            //}

            base.OnGatheringNodeData(e);
        }

        /// <summary>
        /// Override to set a custom field value type for special fields
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        protected override IndexFieldValueTypes CreateFieldValueTypes(Directory x)
        {
            //add custom field definition
            IndexFieldDefinitions.TryAdd(NodeTypeAliasFieldName, new IndexField
            {
                Name = NodeTypeAliasFieldName,
                Type = "culture-invariant-whitespace"
            });
            var result = base.CreateFieldValueTypes(x);
            //now add the custom value type
            result.ValueTypeFactories.TryAdd("culture-invariant-whitespace", s => new FullTextType(s, customAnalyzer: new CultureInvariantWhitespaceAnalyzer()));
            return result;
        }

        /// <summary>
        /// Overridden to add the path property to the special fields to index
        /// </summary>
        /// <param name="d"></param>
        /// <param name="valueSet"></param>
        protected override void AddSpecialFieldsToDocument(Document d, ValueSet valueSet)
        {
            base.AddSpecialFieldsToDocument(d, valueSet);

            //ensure the special path and node type alis fields is added to the dictionary to be saved to file
            if (valueSet.Values.TryGetValue("path", out var pathVals))
            {
                var path = pathVals.FirstOrDefault()?.ToString();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var pathValueType = IndexFieldValueTypes.GetValueType(IndexPathFieldName, IndexFieldValueTypes.ValueTypeFactories[FieldDefinitionTypes.Raw]);
                    pathValueType.AddValue(d, path);
                }
            }
            if (!string.IsNullOrWhiteSpace(valueSet.ItemType))
            {
                var nodeTypeValueType = IndexFieldValueTypes.GetValueType(NodeTypeAliasFieldName, IndexFieldValueTypes.ValueTypeFactories[FieldDefinitionTypes.Raw]);
                nodeTypeValueType.AddValue(d, valueSet.ItemType.ToLowerInvariant());
            }
        }

        /// <summary>
        /// Creates an IIndexCriteria object based on the indexSet passed in and our DataService
        /// </summary>
        /// <param name="indexSet"></param>
        /// <returns></returns>
        protected override IIndexCriteria GetIndexerData(IndexSet indexSet)
        {
            return indexSet.ToIndexCriteria(DataService);
        }

        ///// <summary>
        ///// return the index policy for the field name passed in, if not found, return normal
        ///// </summary>
        ///// <param name="fieldName"></param>
        ///// <returns></returns>
        //protected override FieldIndexTypes GetPolicy(string fieldName)
        //{
        //    var def = IndexFieldPolicies.Where(x => x.Key == fieldName);
        //    return (def.Count() == 0 ? FieldIndexTypes.ANALYZED : def.Single().Value);
        //}

        /// <summary>
        /// Ensure that the content of this node is available for indexing (i.e. don't allow protected
        /// content to be indexed when this is disabled).
        /// <returns></returns>
        /// </summary>
        protected override bool ValidateItem(IndexItem item)
        {
            var nodeId = int.Parse(item.Id);
            // Test for access if we're only indexing published content
            // return nothing if we're not supporting protected content and it is protected, and we're not supporting unpublished content
            if (!SupportUnpublishedContent
                && (!SupportProtectedContent
                && DataService.ContentService.IsProtected(nodeId, item.ValueSet.GetValue("path").ToString())))
            {
                return false;
            }

            return base.ValidateItem(item);
        }
        

        #endregion
    }
}
