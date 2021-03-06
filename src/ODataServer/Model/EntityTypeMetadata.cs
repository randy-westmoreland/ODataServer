﻿// -----------------------------------------------------------------------
// <copyright file="EntityTypeMetadata.cs" company="EntityRepository Contributors" years="2013">
// This software is part of the EntityRepository library.
// Copyright © 2012 EntityRepository Contributors
// http://entityrepository.codeplex.org/
// </copyright>
// -----------------------------------------------------------------------


using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using EntityRepository.ODataServer.Util;
using Microsoft.Data.Edm;

namespace EntityRepository.ODataServer.Model
{
	public class EntityTypeMetadata : IEntityTypeMetadata
	{

		private readonly IEdmEntityType _edmStructuredType;
		private readonly Type _clrType;
		private readonly PropertyInfo[] _clrKeyProperties;
		private readonly Func<object, object> _entityKeyFunction;

		internal EntityTypeMetadata(IEdmEntityType edmStructuredType, Type clrType, PropertyInfo[] clrKeyProperties)
		{
			Contract.Assert(edmStructuredType != null);
			Contract.Assert(clrType != null);
			Contract.Assert(clrKeyProperties != null);
			Contract.Assert(clrKeyProperties.Length >= 1);

			_edmStructuredType = edmStructuredType;
			_clrType = clrType;
			_clrKeyProperties = clrKeyProperties;
			_entityKeyFunction = GetUntypedEntityKeyFunction(this);
		}

		public Type ClrType { get { return _clrType; } }

		public IEdmEntityType EdmType { get { return _edmStructuredType; } }

		public int CountKeyProperties { get { return _clrKeyProperties.Length; } }
		public IEnumerable<IEdmStructuralProperty> EdmKeyProperties { get { return _edmStructuredType.DeclaredKey; } }
		public IEnumerable<PropertyInfo> ClrKeyProperties { get { return _clrKeyProperties; } }

		public Func<object, object> EntityKeyFunction { get { return _entityKeyFunction; } } 

		public PropertyInfo SingleClrKeyProperty
		{
			get
			{
				Contract.Assert(CountKeyProperties == 1);

				return _clrKeyProperties[0];
			}
		}

		public PropertyInfo ClrProperty(IEdmProperty declaredEdmProperty)
		{
			return ClrType.GetProperty(declaredEdmProperty.Name, BindingFlags.Instance | BindingFlags.Public);
		}

		private static Func<object, object> GetUntypedEntityKeyFunction(IEntityTypeMetadata entityTypeMetadata)
		{
			Contract.Assert(entityTypeMetadata != null);

			// Build a parameterized EntityKeyFunction<TEntity, TKey> to obtain the key function
			Type genericEntityKeyFunctionType;
			if (entityTypeMetadata.CountKeyProperties == 1)
			{	// Use the type of the single key
				genericEntityKeyFunctionType = typeof(EntityKeyFunction<,>).MakeGenericType(entityTypeMetadata.ClrType, entityTypeMetadata.SingleClrKeyProperty.PropertyType);
			}
			else
			{	// Use object for an anonymous type - needed for multi property keys
				genericEntityKeyFunctionType = typeof(EntityKeyFunction<,>).MakeGenericType(entityTypeMetadata.ClrType, typeof(object[]));
			}
			return genericEntityKeyFunctionType.InvokeStaticMethod("GetUntypedEntityKeyFunction", entityTypeMetadata) as Func<object, object>;
		}
	}
}