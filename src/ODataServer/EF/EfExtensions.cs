﻿// -----------------------------------------------------------------------
// <copyright file="EfExtensions.cs" company="EntityRepository Contributors" years="2012-2013">
// This software is part of the EntityRepository library.
// Copyright © 2012-2013 EntityRepository Contributors
// http://entityrepository.codeplex.org/
// </copyright>
// -----------------------------------------------------------------------


using System;
using System.Data.Entity;
using System.Data.Entity.Core.Metadata.Edm;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Infrastructure;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Xsl;
using Microsoft.OData.Edm;

namespace EntityRepository.ODataServer.EF
{
	/// <summary>
	/// Extension methods for EF types.
	/// </summary>
	public static class EfExtensions
	{

		private const string c_clrTypeMetadataKey = "http://schemas.microsoft.com/ado/2013/11/edm/customannotation:ClrType";

		private const string c_edmxV4Namespace = "http://docs.oasis-open.org/odata/ns/edmx";

		public static IEdmModel GetEdmModel(this DbContext dbContext)
		{
			// From https://gist.github.com/raghuramn/5864013
			using (MemoryStream stream = new MemoryStream())
			{
				using (XmlWriter writer = XmlWriter.Create(stream))
				{
					EdmxWriter.WriteEdmx(dbContext, writer);
				}

				// TODO: Remove this, debug only
				stream.Seek(0, SeekOrigin.Begin);
				var sr = new StreamReader(stream);
				Debug.WriteLine(sr.ReadToEnd());					

				stream.Seek(0, SeekOrigin.Begin);
				using (XmlReader reader = XmlReader.Create(stream))
				{
					while (reader.Read() && reader.NodeType != XmlNodeType.Element)
					{}

					if (reader.NamespaceURI == c_edmxV4Namespace)
					{
						return Microsoft.OData.Edm.Csdl.EdmxReader.Parse(reader);	
					}
					else
					{
						return ConvertOldEdmxToV4(reader);
					}
				}
			}
		}

		private static IEdmModel ConvertOldEdmxToV4(XmlReader xmlReader)
		{
			var type = typeof(EfExtensions);
			var assembly = type.Assembly;
			var xslStream = assembly.GetManifestResourceStream(type, "V3-to-V4-CSDL.xsl");

			using (var xslReader = XmlReader.Create(xslStream))
			using (MemoryStream outStream = new MemoryStream())
			{
				var xslTransform = new XslCompiledTransform();
				xslTransform.Load(xslReader);

				var xmlWriter = XmlWriter.Create(outStream);
				xslTransform.Transform(xmlReader, xmlWriter);
			}
		}

		/// <summary>
		/// Returns the Clr <see cref="Type"/> for an <see cref="EntityType"/>, property, etc. in C-Space.
		/// </summary>
		/// <param name="conceptualEdmType"></param>
		/// <returns></returns>
		public static Type GetClrType(this EdmType conceptualEdmType)
		{
			MetadataProperty clrTypeMetadata = conceptualEdmType.MetadataProperties.SingleOrDefault(p => p.Name.Equals(c_clrTypeMetadataKey));
			if (clrTypeMetadata == null)
			{
				return null;
			}
			return clrTypeMetadata.Value as Type;
		}

		/// <summary>
		/// Returns the <see cref="PropertyInfo"/> for an <see cref="EdmProperty"/> in C-Space.
		/// </summary>
		/// <param name="conceptualEntityProperty"></param>
		/// <returns></returns>
		public static PropertyInfo GetClrPropertyInfo(this EdmProperty conceptualEntityProperty)
		{
			MetadataProperty clrPropertyMetadata = conceptualEntityProperty.MetadataProperties.SingleOrDefault(p => p.Name.Equals("ClrPropertyInfo"));
			if (clrPropertyMetadata == null)
			{
				return null;
			}
			return clrPropertyMetadata.Value as PropertyInfo;
		}

		internal static ObjectContext GetObjectContext(this DbContext dbContext)
		{
			Contract.Assert(dbContext != null);

			IObjectContextAdapter objectContextAdapter = dbContext;
			return objectContextAdapter.ObjectContext;
		}

		public static void AddEntity(this DbContext dbContext, string entitySetName, object entity)
		{
			Contract.Requires<ArgumentNullException>(dbContext != null);

			dbContext.GetObjectContext().AddObject(entitySetName, entity);
		}

		//internal static IEnumerable<PropertyInfo> GetDbSetProperties(Type dbContextType)
		//{
		//	Type dbsetTypeDefiniction = typeof(DbSet<>);
		//	return dbContextType.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Instance | BindingFlags.Public).Where(p => p.PropertyType.IsGenericType(dbsetTypeDefiniction));
		//}		 

	}
}