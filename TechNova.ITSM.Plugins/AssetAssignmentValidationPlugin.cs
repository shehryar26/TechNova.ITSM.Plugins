using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace TechNova.ITSM.Plugins
{
    public class AssetAssignmentValidationPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            IPluginExecutionContext context =
                (IPluginExecutionContext)serviceProvider.GetService(
                    typeof(IPluginExecutionContext));

            ITracingService tracingService =
                (ITracingService)serviceProvider.GetService(
                    typeof(ITracingService));

            IOrganizationServiceFactory serviceFactory =
                (IOrganizationServiceFactory)serviceProvider.GetService(
                    typeof(IOrganizationServiceFactory));

            IOrganizationService service =
                serviceFactory.CreateOrganizationService(context.UserId);

            tracingService.Trace(
                "AssetAssignmentValidationPlugin execution started.");

            const int RetiredStatus = 126820004;
            const int DisposedStatus = 126820005;
            const int ActiveAssignmentStatus = 126820000;

            if (!string.Equals(
                context.MessageName,
                "Create",
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!context.InputParameters.Contains("Target"))
            {
                return;
            }

            if (!(context.InputParameters["Target"] is Entity target))
            {
                return;
            }

            if (!string.Equals(
                target.LogicalName,
                "tn_assetassignments",
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!target.Contains("tn_asset"))
            {
                throw new InvalidPluginExecutionException(
                    "Asset is required for an Asset Assignment.");
            }

            EntityReference assetReference =
                target.GetAttributeValue<EntityReference>("tn_asset");

            if (assetReference == null)
            {
                throw new InvalidPluginExecutionException(
                    "Please select an Asset before creating the assignment.");
            }

            Guid assetId = assetReference.Id;

            Entity asset = service.Retrieve(
                "tn_asset",
                assetId,
                new ColumnSet("tn_assetstatus"));

            OptionSetValue assetStatus =
                asset.GetAttributeValue<OptionSetValue>("tn_assetstatus");

            if (assetStatus == null)
            {
                throw new InvalidPluginExecutionException(
                    "Asset Status is missing.");
            }

            if (assetStatus.Value == RetiredStatus ||
                assetStatus.Value == DisposedStatus)
            {
                throw new InvalidPluginExecutionException(
                    "This asset cannot be assigned because its status is Retired or Disposed.");
            }

            QueryExpression query =
                new QueryExpression("tn_assetassignments");

            query.ColumnSet = new ColumnSet(
                "tn_assetassignmentsid",
                "tn_employee",
                "tn_assignstatus");

            query.Criteria.AddCondition(
                "tn_asset",
                ConditionOperator.Equal,
                assetId);

            query.Criteria.AddCondition(
                "tn_assignstatus",
                ConditionOperator.Equal,
                ActiveAssignmentStatus);

            EntityCollection existingAssignments =
                service.RetrieveMultiple(query);

            if (existingAssignments.Entities.Count > 0)
            {
                throw new InvalidPluginExecutionException(
                    "This asset already has an active assignment. " +
                    "Please return the existing assignment before assigning this asset again.");
            }
        }
    }
}