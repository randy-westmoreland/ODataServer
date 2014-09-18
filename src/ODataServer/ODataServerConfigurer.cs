﻿// -----------------------------------------------------------------------
// <copyright file="ODataServerConfigurer.cs" company="EntityRepository Contributors" year="2013">
// This software is part of the EntityRepository library
// Copyright © 2012 EntityRepository Contributors
// http://entityrepository.codeplex.org/
// </copyright>
// -----------------------------------------------------------------------

using System.Threading;
using System.Web.OData.Batch;
using System.Web.OData.Extensions;
using System.Web.OData.Routing;
using EntityRepository.ODataServer.Batch;
using EntityRepository.ODataServer.EF;
using EntityRepository.ODataServer.Model;
using EntityRepository.ODataServer.Routing;
using Microsoft.OData.Edm;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.OData;
using System.Web.OData.Builder;
using System.Web.OData.Routing.Conventions;

namespace EntityRepository.ODataServer
{
	/// <summary>
	/// Top level class for managing Web API configuration for <c>EntityRepository.ODataServer</c>.
	/// </summary>
	public class ODataServerConfigurer
	{

		/// <summary>Starter size for collections holding EntitySets</summary>
		internal const int InitialEntitySetCapacity = 30;

		// All controllers must derive from this class.
		public static readonly Type RequiredBaseController = typeof(ODataController);

		private readonly HttpConfiguration _webApiConfig;

		private readonly IContainerMetadata _containerMetadata;

		private readonly EntityRepositoryControllerSelector _controllerSelector;

		public ODataServerConfigurer(HttpConfiguration webApiConfig, IContainerMetadata containerMetadata)
		{
			Contract.Requires<ArgumentNullException>(webApiConfig != null);

			_webApiConfig = webApiConfig;
			_containerMetadata = containerMetadata;

			_controllerSelector = EntityRepositoryControllerSelector.Install(webApiConfig, this);
		}

		/// <summary>
		/// Add an entity set controller of type <paramref name="controllerType"/> for the specified <paramref name="entitySetName"/> and <paramref name="entityType"/>.
		/// </summary>
		/// <param name="entitySetName"></param>
		/// <param name="entityType"></param>
		/// <param name="controllerType"></param>
		public void AddEntitySetController(string entitySetName, Type entityType, Type controllerType)
		{
			Contract.Requires<ArgumentException>(! string.IsNullOrWhiteSpace(entitySetName));
			Contract.Requires<ArgumentNullException>(entityType != null);
			Contract.Requires<ArgumentNullException>(controllerType != null);
			Contract.Requires<ArgumentException>(RequiredBaseController.IsAssignableFrom(controllerType), "Controller types must derive from System.Web.Http.OData.ODataController");

			IEntitySetMetadata entitySetMetadata = _containerMetadata.GetEntitySet(entitySetName);
			if (entitySetMetadata == null)
			{
				throw new ArgumentException(string.Format("EntitySet named '{0}' not found in container.", entitySetName));
			}
			if (entitySetMetadata.ElementTypeMetadata.ClrType != entityType)
			{
				throw new ArgumentException(string.Format("EntitySet named '{0}' should have type {1}; {2} was passed in.", entitySetName, entitySetMetadata.ElementTypeMetadata.ClrType, entityType));
			}

			// HACK: But I don't see a better way.
			// Store the _containerMetadata in a threadlocal, so it can be used by UseEntityRepositoryActionSelectorAttribute.Initialize().
			RoutingExtensions.InitializingContainerMetadata.Value = _containerMetadata;

			var controllerDescriptor = new HttpControllerDescriptor(_webApiConfig, entitySetName, controllerType);
			controllerDescriptor.CacheContainerMetadata(_containerMetadata);
			_controllerSelector.AddController(entitySetName, controllerDescriptor);

			RoutingExtensions.InitializingContainerMetadata.Value = null;
		}

		/// <summary>
		/// A function type that returns the instantiable controller type to use for a given <paramref name="entityType"/>, <paramref name="keyType"/>, and <paramref name="contextType"/>
		/// </summary>
		/// <param name="entityType"></param>
		/// <param name="keyTypes"></param>
		/// <param name="contextType">A context type for an entityset controller; may be <c>null</c>.</param>
		/// <returns></returns>
		public delegate Type ControllerTypeSelector(Type entityType, Type[] keyTypes, Type contextType);

		/// <summary>
		/// The default implementation of <see cref="ControllerTypeSelector"/>.
		/// </summary>
		/// <param name="entityType"></param>
		/// <param name="keyTypes"></param>
		/// <param name="contextType"></param>
		/// <returns></returns>
		public static Type DefaultControllerTypeSelector(Type entityType, Type[] keyTypes, Type contextType)
		{
			if (typeof(DbContext).IsAssignableFrom(contextType))
			{
				if (keyTypes.Length != 1)
				{
					throw new ArgumentException("No default controller exists that supports multiple keys.");
				}
				// Default controller for DbContext containers is EditDbSetController
				return typeof(EditDbSetController<,,>).MakeGenericType(entityType, keyTypes[0], contextType);
			}
			else
			{
				throw new ArgumentException(string.Format("No default controller exists for contextType {0}, entityType {1}", contextType, entityType));
			}			
		}

		public void AddStandardEntitySetControllers()
		{
			AddStandardEntitySetControllers(DefaultControllerTypeSelector);
		}

		/// <summary>
		/// For each entityset defined in the datamodel, adds a standard controller if a custom controller is not already defined.
		/// </summary>
		/// <param name="controllerTypeSelector"></param>
		public void AddStandardEntitySetControllers(ControllerTypeSelector controllerTypeSelector)
		{
			Contract.Requires<ArgumentNullException>(controllerTypeSelector != null);

			// All existing controllers are defined in the fallback controller
			IDictionary<string, HttpControllerDescriptor> existingControllers = _controllerSelector.GetFallbackControllerMapping();

			foreach (IEntitySetMetadata entitySetMetadata in _containerMetadata.EntitySets)
			{
				// Only add the standard controller if there's not already a controller with the same name defined
				string entitySetName = entitySetMetadata.Name;

				HttpControllerDescriptor controllerDescriptor;
				if (! existingControllers.TryGetValue(entitySetName, out controllerDescriptor))
				{
					var entityTypeMetadata = entitySetMetadata.ElementTypeMetadata;

					// Determine the controller type
					Type[] keyTypes = entityTypeMetadata.ClrKeyProperties.Select(prop => prop.PropertyType).ToArray();
					Type controllerType = controllerTypeSelector(entityTypeMetadata.ClrType, keyTypes, entitySetMetadata.ContextType);

					if (controllerType != null)
					{
						AddEntitySetController(entitySetName, entityTypeMetadata.ClrType, controllerType);
					}
				}
			}
		}

		/// <summary>
		/// Configure the OData routes to use the functionality provided in this library.
		/// </summary>
		/// <param name="routes"></param>
		/// <param name="routeName"></param>
		/// <param name="routePrefix"></param>
		/// <param name="httpServer"></param>
		public void ConfigureODataRoutes(HttpConfiguration httpConfiguration, string routeName, string routePrefix, HttpServer httpServer)
		{
			httpConfiguration.MapODataServiceRoute(routeName,
			                                       routePrefix,
			                                       BuildEdmModel(),
			                                       new DefaultODataPathHandler(),
			                                       GetRoutingConventions(),
			                                       new ODataServerBatchHandler(httpServer));
		}


		/// <summary>
		/// Builds the <see cref="IEdmModel"/> for the odata service based on the entityset controllers that have been configured.
		/// </summary>
		/// <returns></returns>
		public IEdmModel BuildEdmModel()
		{
			ODataConventionModelBuilder modelBuilder = new ODataConventionModelBuilder(_webApiConfig);

			ConfigureModelBuilder(modelBuilder);

			//var projectsConfig = modelBuilder.EntitySet<Project>("Projects");
			//projectsConfig.HasManyBinding(p => p.Owners, modelBuilder.EntitySet<User>("Users"));
			//projectsConfig.HasManyBinding(p => p.Areas, modelBuilder.EntitySet<ProjectArea>("ProjectAreas"));
			//projectsConfig.HasManyBinding(p => p.Versions, modelBuilder.EntitySet<ProjectVersion>("ProjectVersions"));
			//projectsConfig.HasManyBinding(p => p.WorkItems, modelBuilder.EntitySet<WorkItem>("WorkItems"));

			return modelBuilder.GetEdmModel();
		}

		/// <summary>
		/// If a caller wants to tweak the <see cref="ODataModelBuilder"/>, this method can be used instead of <see cref="BuildEdmModel"/>,
		/// so that the caller can modify <paramref name="modelBuilder"/> before or after the entity sets are added.
		/// </summary>
		/// <param name="modelBuilder">An <see cref="ODataModelBuilder"/> or <see cref="ODataConventionModelBuilder"/>.</param>
		public void ConfigureModelBuilder(ODataModelBuilder modelBuilder)
		{
			modelBuilder.ContainerName = _containerMetadata.Name;
			modelBuilder.Namespace = _containerMetadata.Namespace;

			// Add all entity types
			foreach (IEntityTypeMetadata entityTypeMetadata in _containerMetadata.EntityTypes)
			{
				EntityTypeConfiguration entityTypeConfig = modelBuilder.AddEntityType(entityTypeMetadata.ClrType);
			}

			// Add all entity sets
			foreach (IEntitySetMetadata entitySetMetadata in _containerMetadata.EntitySets)
			{
				string entitySetName = entitySetMetadata.Name;
				EntityTypeConfiguration entityTypeConfig = (EntityTypeConfiguration) modelBuilder.GetTypeConfigurationOrNull(entitySetMetadata.ElementTypeMetadata.ClrType);
				EntitySetConfiguration entitySetConfig = modelBuilder.AddEntitySet(entitySetName, entityTypeConfig);
			}
		}

		/// <summary>
		/// Returns the standard routing conventions, plus some common routing conventions needed for the controllers in this library to work.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<IODataRoutingConvention> GetRoutingConventions()
		{
			IList<IODataRoutingConvention> routingConventions = ODataRoutingConventions.CreateDefault();
			routingConventions.Insert(0, new GenericNavigationPropertyRoutingConvention());
			return routingConventions;
		}

	}
}
