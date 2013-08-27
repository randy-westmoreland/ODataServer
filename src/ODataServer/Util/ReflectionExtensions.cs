﻿// -----------------------------------------------------------------------
// <copyright file="ReflectionExtensions.cs" company="EntityRepository Contributors" year="2013">
// This software is part of the EntityRepository library
// Copyright © 2012 EntityRepository Contributors
// http://entityrepository.codeplex.org/
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;

namespace EntityRepository.ODataServer.Util
{
	/// <summary>
	/// Extension methods for reflection.
	/// </summary>
	public static class ReflectionExtensions
	{

		/// <summary>
		/// Returns true if <c>this</c> is a generic version of generic type definition <paramref name="genericTypeDefinition"/>.
		/// </summary>
		/// <param name="type"></param>
		/// <param name="genericTypeDefinition"></param>
		/// <returns></returns>
		public static bool IsGenericType(this Type type, Type genericTypeDefinition)
		{
			return type.IsGenericType
			       && ReferenceEquals(type.GetGenericTypeDefinition(), genericTypeDefinition);
		}

		/// <summary>
		/// Returns true if <c>this</c> is a generic version of, or inherits from, generic type definition <paramref name="genericTypeDefinition"/>.
		/// </summary>
		/// <param name="type"></param>
		/// <param name="genericTypeDefinition"></param>
		/// <returns></returns>
		public static bool IsDerivedFromGenericType(this Type type, Type genericTypeDefinition)
		{
			if (type == null)
			{
				return false;
			}
			if (genericTypeDefinition == null)
			{
				return false;
			}

			if (type.IsGenericType)
			{
				Type genericType = type.GetGenericTypeDefinition();
				if (ReferenceEquals(genericType, genericTypeDefinition))
				{
					return true;
				}
			}
			// Recurse
			return IsDerivedFromGenericType(type.BaseType, genericTypeDefinition);
		}

		/// <summary>
		/// Returns the generic type parameters for type <paramref name="genericTypeDefinition"/>
		/// </summary>
		/// <param name="type"></param>
		/// <param name="genericTypeDefinition"></param>
		/// <returns></returns>
		public static Type[] GetGenericTypeParametersForBaseClass(this Type type, Type genericTypeDefinition)
		{
			throw new NotImplementedException();
		}

		public static object GetPropertyValue(this Type type, object instance, string propertyName)
		{
			Contract.Requires<ArgumentNullException>(type != null);
			Contract.Requires<ArgumentNullException>(propertyName != null);
			Contract.Requires<ArgumentException>(!type.IsGenericTypeDefinition);

			BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.FlattenHierarchy;
			if (instance == null)
			{
				bindingFlags |= BindingFlags.Static;
			}
			else
			{
				bindingFlags |= BindingFlags.Instance;
			}
			PropertyInfo propertyInfo = type.GetProperty(propertyName, bindingFlags);
			return propertyInfo.GetValue(instance, null);
		}

		/// <summary>
		/// Invokes a non-generic method using reflection and the specified parameters.
		/// </summary>
		/// <param name="type"></param>
		/// <param name="instance"></param>
		/// <param name="methodName"></param>
		/// <param name="arguments"></param>
		/// <returns></returns>
		public static object InvokeMethod(this Type type, object instance, string methodName, params object[] arguments)
		{
			Contract.Requires<ArgumentNullException>(type != null);
			Contract.Requires<ArgumentNullException>(methodName != null);
			Contract.Requires<ArgumentException>(! type.IsGenericTypeDefinition);

			BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.FlattenHierarchy;
			if (instance == null)
			{
				bindingFlags |= BindingFlags.Static;
			}
			else
			{
				bindingFlags |= BindingFlags.Instance;
			}
			MethodInfo methodInfo = type.GetMethod(methodName, bindingFlags);
			return methodInfo.Invoke(instance, arguments);
		}

		/// <summary>
		/// Invokes a generic method using reflection and the specified parameters.
		/// </summary>
		/// <param name="type"></param>
		/// <param name="instance"></param>
		/// <param name="methodName"></param>
		/// <param name="genericMethodParameters">The generic type arguments.</param>
		/// <param name="arguments"></param>
		/// <returns></returns>
		public static object InvokeMethod(this Type type, object instance, string methodName, Type[] genericMethodParameters, params object[] arguments)
		{
			Contract.Requires<ArgumentNullException>(type != null);
			Contract.Requires<ArgumentNullException>(methodName != null);
			Contract.Requires<ArgumentNullException>(genericMethodParameters != null);
			Contract.Requires<ArgumentException>(!type.IsGenericTypeDefinition);

			BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.FlattenHierarchy;
			if (instance == null)
			{
				bindingFlags |= BindingFlags.Static;
			}
			else
			{
				bindingFlags |= BindingFlags.Instance;
			}

			try
			{
				MethodInfo methodInfo =
					type.GetMethods(bindingFlags)
					    .Where(mi => (mi.Name == methodName) && mi.IsGenericMethodDefinition && (mi.GetGenericArguments().Length == genericMethodParameters.Length)
					                                         && (mi.GetParameters().Length == arguments.Length))
					    .Select(mi => mi.MakeGenericMethod(genericMethodParameters))
					    .First();
				return methodInfo.Invoke(instance, arguments);
			}
			catch (InvalidOperationException excp)
			{
				string msg = string.Format("No public method on Type {0} found named {1} with type parameters {2} and {3} arguments.",
				                           type,
				                           methodName,
				                           string.Join<Type>(", ", genericMethodParameters),
				                           arguments.Length);
				throw new InvalidOperationException(msg, excp);
			}
		}

	}
}