﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Relax
{
    public interface IPrimayOperator<TEntity, TKey> : ISecondaryOperator<TEntity, TKey> where TEntity : class
    {
        IPrimaryExpression<TEntity> Eq(TKey value);
    }

    public interface IPrimaryExpression<TEntity> : ISecondaryExpression<TEntity> where TEntity : class
    {
        new IPrimayOperator<TEntity, TKey> And<TKey>(Expression<Func<TEntity, TKey>> xp);
    }

    public interface ISecondaryOperator<TEntity, TKey> : ITertiaryOperator<TEntity, TKey> where TEntity : class
    {
        ISecondaryExpression<TEntity> Bw(TKey lower, TKey upper);
        ISecondaryExpression<TEntity> Ge(TKey value);
    }

    public interface ISecondaryExpression<TEntity> : ITertiaryExpression<TEntity> where TEntity : class
    {
        new ISecondaryOperator<TEntity, TKey> And<TKey>(Expression<Func<TEntity, TKey>> xp);
    }

    public interface ITertiaryOperator<TEntity, TKey> where TEntity : class
    {
        ITertiaryExpression<TEntity> Le(TKey value);
    }

    public interface ITertiaryExpression<TEntity> where TEntity : class
    {
        ITertiaryOperator<TEntity, TKey> And<TKey>(Expression<Func<TEntity, TKey>> xp);

        Query<TEntity>.Spec Spec();
        Query<TEntity>.Result List();
    }

    public partial class Repository<TEntity>
    {
        private class PrimaryExpression<TKey> : SecondaryExpression<TKey>, IPrimayOperator<TEntity, TKey>, IPrimaryExpression<TEntity>
        {
            public PrimaryExpression(ExpressionValues values) : base(values)
            { 
            }

            public IPrimaryExpression<TEntity> Eq(TKey value)
            {
                (Values.Startkey ?? (Values.Startkey = new JArray())).Add(value);
                (Values.Endkey ?? (Values.Endkey = new JArray())).Add(value);
                return this;
            }

            public new IPrimayOperator<TEntity, TKey2> And<TKey2>(Expression<Func<TEntity, TKey2>> xp)
            {
                return new PrimaryExpression<TKey2>(Values.AppendExpression(xp.Body));
            }
        }

        private class SecondaryExpression<TKey> : TertiaryExpression<TKey>, ISecondaryOperator<TEntity, TKey>, ISecondaryExpression<TEntity>
        {
            protected SecondaryExpression(ExpressionValues values) : base(values)
            {
            }

            public ISecondaryExpression<TEntity> Bw(TKey lower, TKey upper)
            {
                (Values.Startkey ?? (Values.Startkey = new JArray())).Add(lower);
                (Values.Endkey ?? (Values.Endkey = new JArray())).Add(upper);
                return this;
            }   

            public ISecondaryExpression<TEntity> Ge(TKey value)
            {
                (Values.Startkey ?? (Values.Startkey = new JArray())).Add(value);
                (Values.Endkey ?? (Values.Endkey = new JArray())).Add(null);
                return this;
            }

            public new ISecondaryOperator<TEntity, TKey2> And<TKey2>(Expression<Func<TEntity, TKey2>> xp)
            {
                return new SecondaryExpression<TKey2>(Values.AppendExpression(xp.Body));
            }
        }

        private class TertiaryExpression<TKey> : ITertiaryOperator<TEntity, TKey>, ITertiaryExpression<TEntity>
        {
            protected ExpressionValues Values { get; private set; }

            protected TertiaryExpression(ExpressionValues values)
            {
                Values = values;
            }

            public ITertiaryExpression<TEntity> Le(TKey value)
            {
                (Values.Startkey ?? (Values.Startkey = new JArray())).Add(null);
                (Values.Endkey ?? (Values.Endkey = new JArray())).Add(value);
                return this;
            }

            public ITertiaryOperator<TEntity, TKey2> And<TKey2>(Expression<Func<TEntity, TKey2>> xp)
            {
                return new TertiaryExpression<TKey2>(Values.AppendExpression(xp.Body));
            }

            public Query<TEntity>.Spec Spec()
            {
                return Values.CreateQuerySpec(Values.CreateQuery());
            }

            public Query<TEntity>.Result List()
            {
                Spec().Execute();
            }
        }

        private class ExpressionValues
        {
            public Repository<TEntity> Repository;
            public List<string> Fields;
            public JArray Startkey;
            public JArray Endkey;

            public ExpressionValues AppendExpression(Expression x)
            {
                var js = BuildJavasriptExpressionFromLinq(x);
                if (Fields.Contains(js))
                {
                    throw new ArgumentException("Repository.Where() can accept a field only once. The field '" + x.ToString() + "' appears more than once.");
                }
                Fields.Add(js);
                return this;
            }

            public Design.View CreateView()
            {
                var a = new StringBuilder(Fields.Count*32);
                a.Append("function(doc) {");
                a.Append("\n if (doc._id.indexOf('");
                a.Append(typeof (TEntity).Name.ToLowerInvariant());
                a.Append("-') === 0) {");
                a.Append("\n  emit([doc");
                a.Append(Fields[0]);
                for (int n = 1; n < Fields.Count; n++)
                {
                    a.Append(", ");
                    a.Append(Fields[n]);
                }
                a.Append("], null);");
                a.Append("\n }");
                a.Append("\n}\n");

                return new Design.View {Map = a.ToString()};
            }

            public Query<TEntity> CreateQuery()
            {
                // build a view name
                var a = new StringBuilder(16 * Fields.Count);
                a.Append("by");
                foreach (var f in Fields)
                {
                    foreach (var c in f)
                    {
                        a.Append(char.IsLetter(c) ? c : '-');
                    }
                }

                //Repository.Design   
            }

            public Query<TEntity>.Spec CreateQuerySpec(Query<TEntity> query)
            {
                var spec = new Query<TEntity>.Spec(query);
                if (null != Startkey) spec = spec.From(Startkey);
                if (null != Endkey) spec = spec.To(Endkey);
                return spec;
            }
        }

        private static string BuildJavasriptExpressionFromLinq(Expression x)
        {
            var js = "";

            while (null != x)
            {
                switch (x.NodeType)
                {
                    case ExpressionType.Parameter:
                        return js;

                    case ExpressionType.MemberAccess:

                        var xmember = (MemberExpression)x;
                        js = "." + xmember.Member.Name + js;
                        x = xmember.Expression;
                        break;

                    default:
                        throw new NotSupportedException("Repository.Where() currently only parses simply fields/property descent expressions. You tried to use a " + x.NodeType + " expression.");
                }
            }

            throw new Exception("Unexptected state encountered parsing Repository.Where() expression.");
        }

        public IPrimayOperator<TEntity, TKey> Where<TKey>(Expression<Func<TEntity, TKey>> xp)
        {
            return new PrimaryExpression<TKey>(
                new ExpressionValues
                    {
                        Repository = this,
                        Fields = new List<string> { BuildJavasriptExpressionFromLinq(xp.Body) }
                    }
            );
        }
    }
}