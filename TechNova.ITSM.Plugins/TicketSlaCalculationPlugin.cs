using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace TechNova.ITSM.Plugins
{
    public class TicketSlaCalculationPlugin : IPlugin
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
                "TicketSlaCalculationPlugin execution started.");

            // ---------------------------------------------------------
            // 1. Validate message
            // ---------------------------------------------------------

            if (!string.Equals(
                context.MessageName,
                "Create",
                StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    context.MessageName,
                    "Update",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // ---------------------------------------------------------
            // 2. Get Target
            // ---------------------------------------------------------

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
                "tn_ticket",
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // ---------------------------------------------------------
            // 3. Get Priority
            // ---------------------------------------------------------

            OptionSetValue ticketPriority =
                target.GetAttributeValue<OptionSetValue>("tn_priority");

            // On Update, Priority may not be present in Target.
            // In that case retrieve the existing Ticket.
            if (ticketPriority == null &&
                string.Equals(
                    context.MessageName,
                    "Update",
                    StringComparison.OrdinalIgnoreCase))
            {
                Entity existingTicket = service.Retrieve(
                    "tn_ticket",
                    target.Id,
                    new ColumnSet("tn_priority"));

                ticketPriority =
                    existingTicket.GetAttributeValue<OptionSetValue>(
                        "tn_priority");
            }

            if (ticketPriority == null)
            {
                throw new InvalidPluginExecutionException(
                    "Ticket Priority is required before SLA calculation.");
            }

            tracingService.Trace(
                "Ticket Priority: " + ticketPriority.Value);

            // ---------------------------------------------------------
            // 4. Map Ticket Priority -> SLA Policy Priority
            //
            // Ticket:
            // Low      = 833450000
            // Medium   = 833450001
            // High     = 833450002
            // Critical = 833450003
            //
            // SLA:
            // Urgent    = 192350000
            // Important = 192350001
            // Medium    = 192350002
            // Low       = 192350003
            // ---------------------------------------------------------

            int slaPriorityValue;

            switch (ticketPriority.Value)
            {
                case 833450003: // Ticket Critical
                    slaPriorityValue = 192350000; // SLA Urgent
                    break;

                case 833450002: // Ticket High
                    slaPriorityValue = 192350001; // SLA Important
                    break;

                case 833450001: // Ticket Medium
                    slaPriorityValue = 192350002; // SLA Medium
                    break;

                case 833450000: // Ticket Low
                    slaPriorityValue = 192350003; // SLA Low
                    break;

                default:
                    throw new InvalidPluginExecutionException(
                        "Unsupported Ticket Priority value.");
            }

            tracingService.Trace(
                "Mapped SLA Priority: " + slaPriorityValue);

            // ---------------------------------------------------------
            // 5. Find active SLA Policy
            // ---------------------------------------------------------

            QueryExpression slaQuery =
                new QueryExpression("tn_slapolicy");

            slaQuery.ColumnSet = new ColumnSet(
                "tn_policyname",
                "tn_priority",
                "tn_responsetime",
                "tn_resolutiontime",
                "tn_timeunit",
                "tn_isactive");

            slaQuery.Criteria.AddCondition(
                "tn_priority",
                ConditionOperator.Equal,
                slaPriorityValue);

            slaQuery.Criteria.AddCondition(
                "tn_isactive",
                ConditionOperator.Equal,
                true);

            EntityCollection slaPolicies =
                service.RetrieveMultiple(slaQuery);

            // ---------------------------------------------------------
            // 6. Validate SLA Policy result
            // ---------------------------------------------------------

            if (slaPolicies.Entities.Count == 0)
            {
                throw new InvalidPluginExecutionException(
                    "No active SLA Policy was found for the selected Ticket Priority.");
            }

            if (slaPolicies.Entities.Count > 1)
            {
                throw new InvalidPluginExecutionException(
                    "Multiple active SLA Policies were found for the selected Ticket Priority. " +
                    "Please keep only one active SLA Policy for this priority.");
            }

            Entity slaPolicy = slaPolicies.Entities[0];

            tracingService.Trace(
                "SLA Policy found: " +
                slaPolicy.GetAttributeValue<string>("tn_policyname"));

            // ---------------------------------------------------------
            // 7. Get Resolution Time
            // ---------------------------------------------------------

            int resolutionTime =
                slaPolicy.GetAttributeValue<int>("tn_resolutiontime");

            if (resolutionTime <= 0)
            {
                throw new InvalidPluginExecutionException(
                    "SLA Resolution Time must be greater than zero.");
            }

            // ---------------------------------------------------------
            // 8. Get Time Unit
            // ---------------------------------------------------------

            OptionSetValue timeUnit =
                slaPolicy.GetAttributeValue<OptionSetValue>("tn_timeunit");

            if (timeUnit == null)
            {
                throw new InvalidPluginExecutionException(
                    "SLA Time Unit is required.");
            }

            // ---------------------------------------------------------
            // 9. Calculate SLA Due Date
            // ---------------------------------------------------------

            DateTime baseDateTime = DateTime.UtcNow;

            DateTime slaDueDate;

            switch (timeUnit.Value)
            {
                case 126820000: // Minutes
                    slaDueDate = baseDateTime.AddMinutes(resolutionTime);
                    break;

                case 126820001: // Hours
                    slaDueDate = baseDateTime.AddHours(resolutionTime);
                    break;

                case 126820002: // Days
                    slaDueDate = baseDateTime.AddDays(resolutionTime);
                    break;

                default:
                    throw new InvalidPluginExecutionException(
                        "Unsupported SLA Time Unit value.");
            }

            tracingService.Trace(
                "Calculated SLA Due Date: " +
                slaDueDate.ToString("yyyy-MM-dd HH:mm:ss"));

            // ---------------------------------------------------------
            // 10. Set SLA Policy on Ticket
            // ---------------------------------------------------------

            target["tn_slapolicy"] =
                new EntityReference(
                    "tn_slapolicy",
                    slaPolicy.Id);

            // ---------------------------------------------------------
            // 11. Set SLA Due Date on Ticket
            // ---------------------------------------------------------

            target["tn_sladuedate"] = slaDueDate;

            tracingService.Trace(
                "SLA Policy and SLA Due Date populated successfully.");

            tracingService.Trace(
                "TicketSlaCalculationPlugin execution completed.");
        }
    }
}