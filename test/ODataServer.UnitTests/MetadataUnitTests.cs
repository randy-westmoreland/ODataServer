﻿// // -----------------------------------------------------------------------
// <copyright file="EntityDataModelTests.cs" company="EntityRepository Contributors" year="2013">
// This software is part of the EntityRepository library
// Copyright © 2012-2015 EntityRepository Contributors
// http://entityrepository.codeplex.org/
// </copyright>
// -----------------------------------------------------------------------


using EntityRepository.ODataServer.EF;
using EntityRepository.ODataServer.UnitTests.EStore.DataAccess;
using Microsoft.Data.Edm;
using Microsoft.Data.Edm.Csdl;
using Microsoft.Data.Edm.Validation;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using EntityRepository.ODataServer.Edm;
using Xunit;
using Xunit.Abstractions;

namespace EntityRepository.ODataServer.UnitTests
{

	/// <summary>
	/// Exercises our handling of EDM metadata.
	/// </summary>
	public sealed class EntityDataModelTests
	{

		private readonly ITestOutputHelper _testOutput;

		public EntityDataModelTests(ITestOutputHelper testOutputHelper)
		{
			_testOutput = testOutputHelper;
		}

		[Fact]
		public void DumpStandardDbContextEntityDataModel()
		{
			using (EStoreDb db = new EStoreDb())
			{
				IEdmModel edm = db.GetEdmModel();
				DumpEdm(edm);
			}
		}

		[Fact]
		public void DumpFixedDbContextEntityDataModel()
		{
			string edmXml;

			using (EStoreDb db = new EStoreDb())
			{
				var dbContextMetadata = new DbContextMetadata<EStoreDb>(db);
				IEdmModel edm = dbContextMetadata.EdmModel;

				edm.RemoveClrTypeAnnotations();
				edmXml = DumpEdm(edm);

				//foreach (var elt in edm.SchemaElements)
				//{
				//	var directValueAnnotations = edm.DirectValueAnnotationsManager.GetDirectValueAnnotations(elt).ToArray();
				//	var vocabularyAnnotations = edm.FindDeclaredVocabularyAnnotations(elt).ToArray();
				//	_testOutput.WriteLine("{0} has {1} DirectValueAnnotations; {2} VocabularyAnnotations", elt.FullName(), directValueAnnotations.Length, vocabularyAnnotations.Length);
				//}

			}

			Assert.DoesNotContain("ClrType", edmXml);
			Assert.DoesNotContain("__", edmXml);
		}

		private string DumpEdm(IEdmModel edmModel)
		{
			StringWriter sw = new StringWriter();
			using (XmlWriter xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings() { Indent = true }))
			{
				IEnumerable<EdmError> edmErrors;
				bool success = EdmxWriter.TryWriteEdmx(edmModel, xmlWriter, EdmxTarget.OData, out edmErrors);
				Assert.True(success);
			}

			string edmXml = sw.ToString();
			_testOutput.WriteLine(edmXml);
			return edmXml;
		}

	}

}