// 
//  SessionTests.cs
//  
//  Author:
//       Nick Nystrom <nnystrom@gmail.com>
//  
//  Copyright (c) 2009-2011 Nicholas J. Nystrom
// 
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;
using RedBranch.Hammock.Design;

namespace RedBranch.Hammock.Test
{
    [TestFixture]
    public class SessionTests
    {
        private Connection _cx;
        private Session _sx;
        private Document _doc;

        public class Widget
        {
            public string Name { get; set; }
            public string[] Tags { get; set; }
        }

        public class Doodad
        {
            public string Name { get; set; }
        }

        [TestFixtureSetUp]
        public void FixtureSetup()
        {
            _cx = ConnectionTests.CreateConnection();
            if (_cx.ListDatabases().Contains("relax-session-tests"))
            {
                _cx.DeleteDatabase("relax-session-tests");
            }
            _cx.CreateDatabase("relax-session-tests");
            _sx = _cx.CreateSession("relax-session-tests");

            // create an initial document on a seperate session
            var x = _cx.CreateSession(_sx.Database);
            var w = new Widget {Name = "gizmo", Tags = new[] {"whizbang", "geegollie"}};
            _doc = x.Save(w);
        }

        [Test]
        public void Can_list_documents()
        {
            var ids = _sx.ListDocuments();
            Assert.IsNotNull(ids);
        }

        [Test]
        public void Can_create_entity()
        {
            var w = new Widget {Name = "sproket", Tags = new[] {"big", "small"}};
            var doc = _sx.Save(w);
            Assert.IsTrue(_sx.ListDocuments().Any(x => x.Id == doc.Id && x.Revision == doc.Revision));
        }

        [Test]
        public void Can_delete_entity()
        {
            var w = new Widget { Name = "sproket", Tags = new[] { "big", "small" } };
            var doc = _sx.Save(w);
            _sx.Delete(w);
            Assert.IsFalse(_sx.ListDocuments().Any(x => x.Id == doc.Id && x.Revision == doc.Revision));
        }

        [Test]
        public void Can_update_entity_after_creating_it()
        {
            var w = new Widget { Name = "sproket", Tags = new[] { "big", "small" } };
            var doc = _sx.Save(w);
            var doc2 = _sx.Save(w);
            Assert.AreEqual(doc.Id, doc2.Id);
            Assert.AreNotEqual(doc.Revision, doc2.Revision);
        }
        
        [Test]
        public void Can_load_entity()
        {
            var w = _sx.Load<Widget>(_doc.Id);
            Assert.AreEqual("gizmo", w.Name);
        }

        [Test]
        public void Can_update_loaded_entity()
        {
            var x = _sx.ListDocuments();
            var w = _sx.Load<Widget>(_doc.Id);
            w.Name = new string(w.Name.Reverse().ToArray());
            _sx.Save(w);

            var y = _sx.ListDocuments();
            Assert.AreEqual(x.Count, y.Count);
            Assert.AreNotEqual(
                x.First(z => z.Id == _doc.Id).Revision,
                y.First(z => z.Id == _doc.Id).Revision
            );
        }

        [Test]
        public void Cannot_load_entity_with_wrong_generic_argument()
        {
            _sx.Load<Widget>(_doc.Id);
            Assert.Throws<InvalidCastException>(() => _sx.Load<Doodad>(_doc.Id));
        }

        [Test]
        public void Session_can_save_design_document()
        {
            var d = new DesignDocument { Language = "javascript" };
            _sx.Save(d, "_design/foo");
            Assert.True(_sx.ListDocuments().Any(x => x.Id == "_design/foo"));
        }

        [Test]
        public void Session_can_load_design_document()
        {
            _cx.CreateSession(_sx.Database).Save(
                new DesignDocument { Language = "javascript", },
                "_design/bar"
            );

            var d = _sx.Load<DesignDocument>("_design/bar");

            Assert.IsNotNull(d);
        }

        [Test]
        public void Session_can_delete_design_document()
        {
            _cx.CreateSession(_sx.Database).Save(
                new DesignDocument { Language = "javascript", },
                "_design/baz"
            );

            var d = _sx.Load<DesignDocument>("_design/baz");
            _sx.Delete(d);

            Assert.IsFalse(_sx.ListDocuments().Any(x => x.Id == "_design/baz"));
        }

        [Test]
        public void Session_can_be_reset()
        {
            var s = _cx.CreateSession(_sx.Database);
            var w = new Widget {Name = "wingnut"};
            s.Save(w);
            s.Reset();
            Assert.That(() => s.Delete(w), Throws.InstanceOf<Exception>());
        }

        [Test]
        public void Session_preserves_design_document_when_reset()
        {
            var s = _cx.CreateSession(_sx.Database);
            var d = new DesignDocument {Language = "javascript"};
            s.Save(d, "_design/bang");
            s.Reset();
            var e = s.Load<DesignDocument>("_design/bang");
            Assert.That(e, Is.SameAs(d));
        }

        [Test]
        public void Session_returns_itself_to_connection_when_all_locks_disposed()
        {
            var s = _cx.CreateSession(_sx.Database);
            using (s.Lock())
            {
                using (s.Lock())
                {
                }
                var t = _cx.CreateSession(_sx.Database);
                Assert.That(t, Is.Not.SameAs(s));
            }
            var u = _cx.CreateSession(_sx.Database);
            Assert.That(u, Is.SameAs(s));
        }

        public class DocumentSubclass : Document
        {
            public string Name { get; set; }
        }

        public class IHasDocumentImplementation : IHasDocument
        {
            public Document Document { get; set; }
            public string Name { get; set; }
        }

        [Test]
        public void Session_uses_id_when_saving_document_subclassed_entities()
        {
            // http://code.google.com/p/relax-net/issues/detail?id=7
            var s = _cx.CreateSession(_sx.Database);
            var x = new DocumentSubclass() {Name = "foo", Id = "foo-document-subclass"};
            s.Save(x);
            var y = s.Load<DocumentSubclass>("foo-document-subclass");
            Assert.That(y, Is.SameAs(x));
        }

        [Test]
        public void Session_fills_id_and_revision_when_saving_document_subclassed_entities()
        {
            // http://code.google.com/p/relax-net/issues/detail?id=7
            var s = _cx.CreateSession(_sx.Database);
            var x = new DocumentSubclass();
            s.Save(x);
            Assert.That(x.Id, Is.Not.Empty);
            Assert.That(x.Revision, Is.Not.Empty);
        }

        [Test]
        public void Session_fills_id_and_revision_when_loading_document_subclassed_entities()
        {
            // http://code.google.com/p/relax-net/issues/detail?id=7
            var s = _cx.CreateSession(_sx.Database);
            var x = new DocumentSubclass();
            s.Save(x);

            var t = _cx.CreateSession(_sx.Database);
            var y = t.Load<DocumentSubclass>(x.Id);

            Assert.That(y.Id, Is.EqualTo(x.Id));
            Assert.That(y.Revision, Is.EqualTo(x.Revision));
        }

        [Test]
        public void Session_uses_id_when_saving_ihasdocument_implentations()
        {
            // http://code.google.com/p/relax-net/issues/detail?id=7
            var s = _cx.CreateSession(_sx.Database);
            var x = new IHasDocumentImplementation()
            {
                Name = "bar",
                Document = new Document { Id = "bar-document-subclass" }
            };
            s.Save(x);
            var y = s.Load<IHasDocumentImplementation>("bar-document-subclass");
            Assert.That(y, Is.SameAs(x));
        }

        [Test]
        public void Session_fills_document_property_when_saving_entities_that_implement_ihasdocument()
        {
            // http://code.google.com/p/relax-net/issues/detail?id=7
            var s = _cx.CreateSession(_sx.Database);
            var x = new IHasDocumentImplementation();
            s.Save(x);
            Assert.That(x.Document.Id, Is.Not.Empty);
            Assert.That(x.Document.Revision, Is.Not.Empty);
        }

        [Test]
        public void Session_fills_document_property_when_loading_entities_that_implement_ihasdocument()
        {
            // http://code.google.com/p/relax-net/issues/detail?id=7
            var s = _cx.CreateSession(_sx.Database);
            var x = new IHasDocumentImplementation();
            s.Save(x);

            var t = _cx.CreateSession(_sx.Database);
            var y = t.Load<IHasDocumentImplementation>(x.Document.Id);

            Assert.That(y.Document.Id, Is.EqualTo(x.Document.Id));
            Assert.That(y.Document.Revision, Is.EqualTo(x.Document.Revision));
        }
    }
}
