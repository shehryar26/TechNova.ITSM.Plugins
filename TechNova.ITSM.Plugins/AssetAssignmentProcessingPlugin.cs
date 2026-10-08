using System;
using Microsoft.Xrm.Sdk;

namespace TechNova.ITSM.Plugins
{
    public class AssetAssignmentProcessingPlugin : IPlugin
    {
        // ---------- Tables ----------
        private const string AssignmentTable = "tn_assetassignments";
        private const string AuditTable = "tn_assetaudithistory";

        // ---------- Asset Assignment columns ----------
        private const string AsgAsset = "tn_asset";
        private const string AsgEmployee = "tn_employee";
        private const string AsgStatus = "tn_assignstatus";
        private const string AsgAssignedBy = "tn_assignedby";

        // ---------- Asset column ----------
        private const string AssetStatusField = "tn_assetstatus";

        // ---------- Audit columns ----------
        // ASSUMPTION: primary name column of Audit table is tn_name (verify in Step 2)
        private const string AudName = "tn_auditid";
        private const string AudAsset = "tn_asset";
        private const string AudAction = "tn_action";
        private const string AudPrevEmployee = "tn_previousemployee";
        private const string AudNewEmployee = "tn_newemployee";
        private const string AudPerformedBy = "tn_performedby";
        private const string AudActionDate = "tn_actiondate";
        private const string AudDescription = "tn_description";

        // ---------- Choice values ----------
        private const int AssetAssigned = 126820001;
        private const int AssetAvailable = 126820000;

        private const int AsgActive = 126820000;
        private const int AsgReturned = 126820001;
        // Transferred = 126820002 (next enhancement)

        private const int ActionAssigned = 126820001;
        private const int ActionReturned = 126820002;

        private const string ImageName = "Image";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            if (context.PrimaryEntityName != AssignmentTable) return;
            if (!context.InputParameters.Contains("Target")) return;
            var target = context.InputParameters["Target"] as Entity;
            if (target == null) return;

            int? newStatus = GetStatus(target);

            if (context.MessageName == "Create")
            {
                if (newStatus != AsgActive) return;

                tracing.Trace("Create: Active assignment detected.");

                // Auto-fill Assigned By with current user if empty
                if (!target.Contains(AsgAssignedBy))
                {
                    var upd = new Entity(AssignmentTable, target.Id);
                    upd[AsgAssignedBy] = new EntityReference("systemuser", context.InitiatingUserId);
                    service.Update(upd);
                }

                ProcessAssigned(service, context, tracing, target);
            }
            else if (context.MessageName == "Update")
            {
                if (newStatus == null) return;

                Entity pre = GetImage(context.PreEntityImages);
                Entity post = GetImage(context.PostEntityImages);
                int? oldStatus = pre != null ? GetStatus(pre) : null;

                if (oldStatus == newStatus)
                {
                    tracing.Trace("Status unchanged, skipping.");
                    return;
                }

                Entity full = post ?? pre;
                if (full == null)
                    throw new InvalidPluginExecutionException("Plugin image is missing. Register image 'Image' on the Update step.");

                if (newStatus == AsgActive)
                {
                    ProcessAssigned(service, context, tracing, full);
                }
                else if (newStatus == AsgReturned)
                {
                    ProcessReturned(service, context, tracing, full);
                }
                else
                {
                    tracing.Trace("Status " + newStatus + " not handled yet (Transferred is a later enhancement).");
                }
            }
        }
        

        private void ProcessAssigned(IOrganizationService service, IPluginExecutionContext context,
            ITracingService tracing, Entity assignment)
        {
            var assetRef = assignment.GetAttributeValue<EntityReference>(AsgAsset);
            var employeeRef = assignment.GetAttributeValue<EntityReference>(AsgEmployee);

            if (assetRef == null)
                throw new InvalidPluginExecutionException("Asset is required for an assignment.");

            // Asset -> Assigned
            var asset = new Entity(assetRef.LogicalName, assetRef.Id);
            asset[AssetStatusField] = new OptionSetValue(AssetAssigned);
            service.Update(asset);
            tracing.Trace("Asset set to Assigned.");

            // Audit -> Assigned
            var audit = NewAudit(assetRef, ActionAssigned, "Assigned", context);
            if (employeeRef != null) audit[AudNewEmployee] = employeeRef;
            audit[AudDescription] = "Asset assigned to employee";
            service.Create(audit);
            tracing.Trace("Audit (Assigned) created.");
        }

        private void ProcessReturned(IOrganizationService service, IPluginExecutionContext context,
            ITracingService tracing, Entity assignment)
        {
            var assetRef = assignment.GetAttributeValue<EntityReference>(AsgAsset);
            var employeeRef = assignment.GetAttributeValue<EntityReference>(AsgEmployee);

            if (assetRef == null)
                throw new InvalidPluginExecutionException("Asset is missing on this assignment.");

            // Asset -> Available
            var asset = new Entity(assetRef.LogicalName, assetRef.Id);
            asset[AssetStatusField] = new OptionSetValue(AssetAvailable);
            service.Update(asset);
            tracing.Trace("Asset set to Available.");

            // Audit -> Returned
            var audit = NewAudit(assetRef, ActionReturned, "Returned", context);
            if (employeeRef != null) audit[AudPrevEmployee] = employeeRef;
            audit[AudDescription] = "Asset returned by employee";
            service.Create(audit);
            tracing.Trace("Audit (Returned) created.");
        }

        private Entity NewAudit(EntityReference assetRef, int action, string actionLabel, IPluginExecutionContext context)
        {
            var audit = new Entity(AuditTable);
            
            audit[AudAsset] = assetRef;
            audit[AudAction] = new OptionSetValue(action);
            
            audit[AudPerformedBy] = new EntityReference("systemuser", context.InitiatingUserId);
            audit[AudActionDate] = DateTime.UtcNow;
            return audit;
        }

        private static int? GetStatus(Entity e)
        {
            var os = e.GetAttributeValue<OptionSetValue>(AsgStatus);
            return os != null ? (int?)os.Value : null;
        }

        private static Entity GetImage(EntityImageCollection images)
        {
            if (images != null && images.Contains(ImageName)) return images[ImageName];
            return null;
        }
    }
}