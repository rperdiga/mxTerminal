using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Threading.Tasks;
using Terminal.Interop;
using Terminal.Spmcp.Core;

namespace Terminal.Spmcp.Handlers
{
    public class DeleteRequest
    {
        public string Type { get; set; } = string.Empty; // "entity", "attribute", or "association"
        public string EntityName { get; set; } = string.Empty;
        public string AttributeName { get; set; } = string.Empty;
        public string AssociationName { get; set; } = string.Empty;
    }

    public class DeleteModelHandler : BaseApiHandler
    {
        public override string Path => "/api/model/delete";
        public override string Method => "POST";

        public DeleteModelHandler() : base() { }

        public override async Task HandleAsync(HttpContext context)
        {
            await ExecuteInTransactionAsync(
                context,
                "delete model element",
                async () =>
                {
                    try
                    {
                        var request = await DeserializeRequestBodyAsync<DeleteRequest>(context);

                        if (request == null)
                        {
                            return (
                                success: false,
                                message: "Invalid request format",
                                data: null as object
                            );
                        }

                        var model = HostServices.Model;
                        var domainModel = HostServices.DomainModel;

                        // Resolve the first user module
                        var module = model.ListModules().FirstOrDefault(m =>
                            !m.Name.StartsWith("System", StringComparison.OrdinalIgnoreCase) &&
                            !m.Name.StartsWith("AppStore", StringComparison.OrdinalIgnoreCase));

                        if (module == default)
                        {
                            return (
                                success: false,
                                message: "No domain model found.",
                                data: null as object
                            );
                        }

                        switch (request.Type.ToLower())
                        {
                            case "entity":
                                return DeleteEntity(domainModel, module, request.EntityName);

                            case "attribute":
                                return DeleteAttribute(domainModel, module, request.EntityName, request.AttributeName);

                            case "association":
                                return DeleteAssociation(domainModel, module, request.EntityName, request.AssociationName);

                            default:
                                return (
                                    success: false,
                                    message: $"Unknown deletion type: {request.Type}",
                                    data: null as object
                                );
                        }
                    }
                    catch (Exception ex)
                    {
                        return (
                            success: false,
                            message: $"Error during deletion: {ex.Message}",
                            data: null as object
                        );
                    }
                });
        }

        private (bool success, string message, object? data) DeleteEntity(
            IDomainModelHost domainModel, ModuleId module, string entityName)
        {
            var entities = domainModel.ListEntities(module);
            var entity = entities.FirstOrDefault(e =>
                e.QualifiedName.Split('.').Last().Equals(entityName, StringComparison.OrdinalIgnoreCase));

            if (entity == default)
            {
                return (false, $"Entity '{entityName}' not found", null);
            }

            domainModel.DeleteEntity(entity);

            return (true, $"Entity '{entityName}' and its associations deleted successfully", null);
        }

        private (bool success, string message, object? data) DeleteAttribute(
            IDomainModelHost domainModel, ModuleId module, string entityName, string attributeName)
        {
            var entities = domainModel.ListEntities(module);
            var entity = entities.FirstOrDefault(e =>
                e.QualifiedName.Split('.').Last().Equals(entityName, StringComparison.OrdinalIgnoreCase));

            if (entity == default)
            {
                return (false, $"Entity '{entityName}' not found", null);
            }

            var shape = domainModel.ReadEntity(entity);
            var attribute = shape.Attributes.FirstOrDefault(a =>
                a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));

            if (attribute == default)
            {
                return (false, $"Attribute '{attributeName}' not found in entity '{entityName}'", null);
            }

            domainModel.DeleteAttribute(entity, attribute);

            return (true, $"Attribute '{attributeName}' deleted successfully from entity '{entityName}'", null);
        }

        private (bool success, string message, object? data) DeleteAssociation(
            IDomainModelHost domainModel, ModuleId module, string entityName, string associationName)
        {
            var entities = domainModel.ListEntities(module);
            var entity = entities.FirstOrDefault(e =>
                e.QualifiedName.Split('.').Last().Equals(entityName, StringComparison.OrdinalIgnoreCase));

            if (entity == default)
            {
                return (false, $"Entity '{entityName}' not found", null);
            }

            var shape = domainModel.ReadEntity(entity);
            var association = shape.OutgoingAssociations
                .Concat(shape.IncomingAssociations)
                .FirstOrDefault(a => a.Name.Equals(associationName, StringComparison.OrdinalIgnoreCase));

            if (association == default)
            {
                return (false, $"Association '{associationName}' not found", null);
            }

            domainModel.DeleteAssociation(association);

            return (true, $"Association '{associationName}' deleted successfully", null);
        }
    }
}
