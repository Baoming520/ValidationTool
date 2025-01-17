﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace ODataValidator.Rule
{
    #region Namespaces
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using System.Linq;
    using System.Net;
    using ODataValidator.Rule.Helper;
    using ODataValidator.RuleEngine;
    using ODataValidator.RuleEngine.Common;
    #endregion

    /// <summary>
    /// Class of service implemenation feature to remove a reference to a single-valued naviagion property of the entity.
    /// </summary>
    [Export(typeof(ExtensionRule))]
    public class ServiceImpl_RemoveReferenceToEntity : ServiceImplExtensionRule
    {
        /// <summary>
        /// Gets the service implementation feature name
        /// </summary>
        public override string Name
        {
            get
            {
                return "ServiceImpl_RemoveReferenceToEntity";
            }
        }

        /// <summary>
        /// Gets the service implementation category.
        /// </summary>
        public override ServiceImplCategory CategoryInfo
        {
            get
            {
                var parent = new ServiceImplCategory(ServiceImplCategoryName.DataModification);
                return new ServiceImplCategory(ServiceImplCategoryName.ModifyingRelationships, parent);
            }
        }

        /// <summary>
        /// Gets the service implementation feature description
        /// </summary>
        public override string Description
        {
            get
            {
                return this.CategoryInfo.CategoryFullName + ",Remove a Reference to an Entity";
            }
        }

        /// <summary>
        /// Gets the service implementation feature specification in OData document
        /// </summary>
        public override string V4SpecificationSection
        {
            get
            {
                return "11.4.6.2";
            }
        }

        /// <summary>
        /// Gets the service implementation feature level.
        /// </summary>
        public override RequirementLevel RequirementLevel
        {
            get
            {
                return RequirementLevel.Should;
            }
        }

        /// <summary>
        /// Verifies the service implementation feature.
        /// </summary>
        /// <param name="context">The Interop service context</param>
        /// <param name="info">out parameter to return violation information when rule does not pass</param>
        /// <returns>true if the service implementation feature passes; false otherwise</returns>
        public override bool? Verify(ServiceContext context, out ExtensionRuleViolationInfo info)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            bool? passed = null;
            info = null;
            ServiceStatus serviceStatus = ServiceStatus.GetInstance();
            TermDocuments termDocs = TermDocuments.GetInstance();
            DataFactory dFactory = DataFactory.Instance();
            var detail = new ExtensionRuleResultDetail(this.Name, serviceStatus.RootURL, HttpMethod.Post, string.Empty);
            List<string> keyPropertyTypes = new List<string>() { "Edm.Int32", "Edm.Int16", "Edm.Int64", "Edm.Guid", "Edm.String" };
            var entityTypeElements = MetadataHelper.GetEntityTypes(serviceStatus.MetadataDocument, 1, keyPropertyTypes, null, NavigationRoughType.CollectionValued);

            if (null == entityTypeElements || 0 == entityTypeElements.Count())
            {
                detail.ErrorMessage = "To verify this rule it expects an entity type with Int32/Int64/Int16/Guid/String key property, but there is no this entity type in metadata so can not verify this rule.";
                info = new ExtensionRuleViolationInfo(new Uri(serviceStatus.RootURL), serviceStatus.ServiceDocument, detail);

                return passed;
            }

            string entitySet = string.Empty;
            string navigProp = string.Empty;
            EntityTypeElement entityType = new EntityTypeElement();
            foreach (var en in entityTypeElements)
            {
                entitySet = en.EntitySetName;
                var funcs = new List<Func<string, string, string, List<NormalProperty>, List<NavigProperty>, bool>>() 
                {
                    AnnotationsHelper.GetExpandRestrictions, AnnotationsHelper.GetInsertRestrictions, AnnotationsHelper.GetDeleteRestrictions
                };

                var restrictions = entitySet.GetRestrictions(serviceStatus.MetadataDocument, termDocs.VocCapabilitiesDoc, funcs, null, NavigationRoughType.SingleValued);
                if (!string.IsNullOrEmpty(restrictions.Item1)
                    && null != restrictions.Item2 && restrictions.Item2.Any()
                    && null != restrictions.Item3 && restrictions.Item3.Any())
                {
                    navigProp = restrictions.Item3.First().NavigationPropertyName;
                    entityType = en;
                    break;
                }
            }

            if (string.IsNullOrEmpty(entitySet) || string.IsNullOrEmpty(navigProp))
            {
                detail.ErrorMessage = "Cannot find an entity-set URL which can be executed the deep insert operation on it.";
                info = new ExtensionRuleViolationInfo(new Uri(serviceStatus.RootURL), serviceStatus.ServiceDocument, detail);

                return passed;
            }

            string url = serviceStatus.RootURL.TrimEnd('/') + @"/" + entitySet;
            var additionalInfos = new List<AdditionalInfo>();
            var reqData = dFactory.ConstructInsertedEntityData(entityType.EntitySetName, entityType.EntityTypeShortName, new List<string>() { navigProp }, out additionalInfos);
            string reqDataStr = reqData.ToString();
            bool isMediaType = !string.IsNullOrEmpty(additionalInfos.Last().ODataMediaEtag);
            var resp = WebHelper.CreateEntity(url, context.RequestHeaders, reqData, isMediaType, ref additionalInfos);
            detail = new ExtensionRuleResultDetail(this.Name, url, HttpMethod.Post, string.Empty, resp, string.Empty, reqDataStr);
            if (HttpStatusCode.Created == resp.StatusCode)
            {
                string navigationUrl = additionalInfos.Last().EntityId.TrimEnd('/') + @"/" + navigProp + @"/$ref";
                resp = WebHelper.GetEntity(navigationUrl);
                detail = new ExtensionRuleResultDetail(this.Name, navigationUrl, HttpMethod.Get, string.Empty, resp, string.Empty, reqDataStr);

                if (HttpStatusCode.OK == resp.StatusCode && additionalInfos.Count > 1)
                {
                     resp = WebHelper.DeleteEntity(navigationUrl, context.RequestHeaders, false);

                     if(HttpStatusCode.NoContent == resp.StatusCode)
                    {
                        passed = true;
                        detail = new ExtensionRuleResultDetail(this.Name, navigationUrl, HttpMethod.Delete, string.Empty, resp, string.Empty, null);
                    }
                    else
                    {
                        passed = false;
                        detail.ErrorMessage = "Cannot remove the reference to the entity.";
                    }
                }
                else
                {
                    detail.ErrorMessage = "Can not get the created entity. ";
                }

                // Restore the service.
                var resps = WebHelper.DeleteEntities(context.RequestHeaders, additionalInfos);
            }
            else
            {
                detail.ErrorMessage = "Creating the new entity failed for above URI. ";
            }

            var details = new List<ExtensionRuleResultDetail>() { detail };
            info = new ExtensionRuleViolationInfo(new Uri(url), serviceStatus.ServiceDocument, details);

            return passed;
        }
    }
}
