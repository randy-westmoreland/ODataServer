﻿// -----------------------------------------------------------------------
// <copyright file="EntityRepositoryActionSelector.cs" company="EntityRepository Contributors" years="2012-2013">
// This software is part of the EntityRepository library.
// Copyright © 2012-2013 EntityRepository Contributors
// http://entityrepository.codeplex.org/
// </copyright>
// -----------------------------------------------------------------------


using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.OData.Extensions;
using System.Web.Http.OData.Routing;
using System.Web.Http.OData.Routing.Conventions;
using System.Web.Http.Routing;
using EntityRepository.ODataServer.Batch;
using EntityRepository.ODataServer.Model;
using Microsoft.Data.Edm;

namespace EntityRepository.ODataServer.Routing
{
	/// <summary>
	/// An implementation of <see cref="IHttpActionSelector"/> that uses <see cref="IODataRoutingConvention"/>s plus additional generic support to select an action for OData requests.
	/// </summary>
	public class EntityRepositoryActionSelector : IHttpActionSelector
	{

		private readonly IContainerMetadata _containerMetadata;
		private readonly IHttpActionSelector _innerSelector;

		/// <summary>
		/// Initializes a new instance of the <see cref="EntityRepositoryActionSelector" /> class.
		/// </summary>
		/// <param name="containerMetadata">The container metadata describing all the entity sets in the container.</param>
		/// <param name="innerSelector">The inner controller selector to call.</param>
		public EntityRepositoryActionSelector(IContainerMetadata containerMetadata, IHttpActionSelector innerSelector)
		{
			if (containerMetadata == null)
			{
				throw new ArgumentNullException("containerMetadata");
			}
			if (innerSelector == null)
			{
				throw new ArgumentNullException("innerSelector");
			}

			_containerMetadata = containerMetadata;
			_innerSelector = innerSelector;
		}

		#region IHttpActionSelector

		/// <summary>
		/// Returns a map, keyed by action string, of all <see cref="T:System.Web.Http.Controllers.HttpActionDescriptor" /> that the selector can select.  This is primarily called by <see cref="T:System.Web.Http.Description.IApiExplorer" /> to discover all the possible actions in the controller.
		/// </summary>
		/// <param name="controllerDescriptor">The controller descriptor.</param>
		/// <returns>
		/// A map of <see cref="T:System.Web.Http.Controllers.HttpActionDescriptor" /> that the selector can select, or null if the selector does not have a well-defined mapping of <see cref="T:System.Web.Http.Controllers.HttpActionDescriptor" />.
		/// </returns>
		/// <exception cref="System.NotImplementedException"></exception>
		public ILookup<string, HttpActionDescriptor> GetActionMapping(HttpControllerDescriptor controllerDescriptor)
		{
			var innerMapping = _innerSelector.GetActionMapping(controllerDescriptor);
			if (innerMapping.Contains(GenericNavigationPropertyRoutingConvention.PostNavigationPropertyMethodName))
			{
				// TODO: Cache per controller type - this is probably pretty expensive.
				return ExpandGenericNavigationPropertyActions(innerMapping);
			}
			else
			{
				return innerMapping;
			}
		}

		/// <summary>
		/// Selects an action for the <see cref="ApiControllerActionSelector" />.
		/// </summary>
		/// <param name="controllerContext">The controller context.</param>
		/// <returns>
		/// The selected action.
		/// </returns>
		[SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Response disposed later")]
		public HttpActionDescriptor SelectAction(HttpControllerContext controllerContext)
		{
			if (controllerContext == null)
			{
				throw new ArgumentNullException("controllerContext");
			}

			HttpRequestMessage request = controllerContext.Request;
			ODataPath odataPath = request.ODataProperties().Path;
			IEnumerable<IODataRoutingConvention> routingConventions = request.ODataProperties().RoutingConventions;
			IHttpRouteData routeData = controllerContext.RouteData;

			if (odataPath == null || routingConventions == null || routeData.Values.ContainsKey(ODataRouteConstants.Action))
			{
				return _innerSelector.SelectAction(controllerContext);
			}

			ILookup<string, HttpActionDescriptor> actionMap = GetActionMapping(controllerContext.ControllerDescriptor);
			foreach (IODataRoutingConvention routingConvention in routingConventions)
			{
				string actionName = routingConvention.SelectAction(odataPath, controllerContext, actionMap);
				if (actionName != null)
				{
					routeData.Values[ODataRouteConstants.Action] = actionName;
					IEnumerable<HttpActionDescriptor> candidateActions = actionMap[actionName];
					int countMatchingActions = candidateActions.Count();
					if (countMatchingActions == 1)
					{
						HttpActionDescriptor selectedCandidate = candidateActions.First();
						return selectedCandidate;
					}
					else if (countMatchingActions == 0)
					{
						return null;
					}

					if (candidateActions.Any(candidateAction => ChangeSetEntityModelBinder.ActionHasChangeSetEntityParameter(candidateAction)))
					{
						IEnumerable<HttpActionDescriptor> orderedActions = candidateActions.OrderBy(DetermineActionOrder);
						// TODO: IDEAL: Select the first action with all parameters present
						// Since I can't figure out a good way to determine that, this implementation returns
						// the changeSetEntity action iff the request URI has a ContentId reference;
						// otherwise it returns the next available action.
						foreach (var candidateAction in orderedActions)
						{
							if (ChangeSetEntityModelBinder.ActionHasChangeSetEntityParameter(candidateAction))
							{
								if (ContentIdHelper.RequestHasContentIdReference(controllerContext.Request))
								{
									return candidateAction;
								}
								// else continue;
							}
							else
							{
								return candidateAction;
							}
						}
					}
					else
					{
						// If there aren't any ChangeSet entity parameters in any of the candidate actions, just use the regular 
						// HttpActionSelector's implementation. It does a good job selecting the best match based on parameters, but it doesn't support
						// using an expanded set of actions.
						return _innerSelector.SelectAction(controllerContext);
					}
				}
			}

			throw new HttpResponseException(request.CreateErrorResponse(HttpStatusCode.NotFound,
			                                                            string.Format("No matching resource found for {0}", controllerContext.Request.RequestUri)));
		}

		#endregion

		private ILookup<string, HttpActionDescriptor> ExpandGenericNavigationPropertyActions(ILookup<string, HttpActionDescriptor> innerMapping)
		{
			List<HttpActionDescriptor> removeDescriptors = new List<HttpActionDescriptor>();
			List<HttpActionDescriptor> expandedDescriptors = new List<HttpActionDescriptor>();

			// Expand generic methods named "PostNavigationProperty" to all supported types
			ExpandGenericNavigationActionsForAllNavigationProperties(innerMapping[GenericNavigationPropertyRoutingConvention.PostNavigationPropertyMethodName],
			                                                         expandedDescriptors,
			                                                         removeDescriptors,
			                                                         navPropertyName => "Post" + navPropertyName);
			ExpandGenericNavigationActionsForAllNavigationProperties(innerMapping[GenericNavigationPropertyRoutingConvention.GetNavigationPropertyMethodName],
			                                                         expandedDescriptors,
			                                                         removeDescriptors,
			                                                         navPropertyName => "Get" + navPropertyName);

			if (expandedDescriptors.Count == 0)
			{
				// No additions
				return innerMapping;
			}
			else
			{
				// Combine inner action descriptors plus expanded action descriptors
				return expandedDescriptors.Concat(innerMapping.SelectMany(g => g).Except(removeDescriptors))
										  .ToLookup(httpActionDescriptor => httpActionDescriptor.ActionName);
			}
		}

		private static void ExpandGenericNavigationActionsForAllNavigationProperties(IEnumerable<HttpActionDescriptor> navigationActions,
		                                                                             List<HttpActionDescriptor> expandedDescriptors,
		                                                                             List<HttpActionDescriptor> removeDescriptors,
		                                                                             Func<string, string> actionNameBuilder)
		{
			foreach (HttpActionDescriptor navigationAction in navigationActions)
			{
				ReflectedHttpActionDescriptor reflectedHttpActionDescriptor = navigationAction as ReflectedHttpActionDescriptor;
				if ((reflectedHttpActionDescriptor != null) &&
					reflectedHttpActionDescriptor.MethodInfo.IsGenericMethodDefinition &&
					reflectedHttpActionDescriptor.MethodInfo.GetGenericArguments().Length == 1)
				{
					// Lookup the EntitySet metadata for the controller
					IContainerMetadata containerMetadata = reflectedHttpActionDescriptor.ControllerDescriptor.GetContainerMetadata();
					if (containerMetadata != null)
					{
						IEntitySetMetadata entitySetMetadata = containerMetadata.GetEntitySet(reflectedHttpActionDescriptor.ControllerDescriptor.ControllerName);
						if (entitySetMetadata != null)
						{
							foreach (IEntityTypeMetadata entityTypeMetadata in entitySetMetadata.ElementTypeHierarchyMetadata)
							{
								// Foreach NavigationProperty in all of the entity types, add a new HttpActionDescriptor
								foreach (var edmNavigationProperty in entityTypeMetadata.EdmType.DeclaredProperties.OfType<IEdmNavigationProperty>())
								{
									IEdmEntityType toEntityType = edmNavigationProperty.ToEntityType();
									IEntityTypeMetadata propertyTypeMetadata = containerMetadata.GetEntityType(toEntityType);

									Type tProperty = propertyTypeMetadata.ClrType;
									MethodInfo genericMethod = reflectedHttpActionDescriptor.MethodInfo.MakeGenericMethod(tProperty);
									string expandedActionName = actionNameBuilder(edmNavigationProperty.Name);
									expandedDescriptors.Add(new RenamedReflectedHttpActionDescriptor(reflectedHttpActionDescriptor.ControllerDescriptor,
									                                                                 genericMethod,
									                                                                 expandedActionName));
								}
							}
						}
					}

					removeDescriptors.Add(reflectedHttpActionDescriptor);
				}
			}
		}

		/// <summary>
		/// Determines the relative order of an action - used to select the lowest priority action in <see cref="SelectAction"/>.
		/// </summary>
		/// <param name="actionDescriptor"></param>
		/// <returns></returns>
		private static int DetermineActionOrder(HttpActionDescriptor actionDescriptor)
		{
			// Hard-coded rule: An action with first parameter "TEntity entity" is order 1
			// All others are order 2
			var parameters = actionDescriptor.GetParameters();
			if (parameters.Count >= 1)
			{
				if (ChangeSetEntityModelBinder.IsChangeSetEntityParameter(parameters[0]))
				{
					// Takes precedence
					return 1;
				}
			}

			return 2;
		}
	}
}