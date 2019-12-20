namespace Kritikos.PureMapper
{
	using System;
	using System.Collections.Generic;
	using System.Linq.Expressions;
	using System.Reflection;
	using Kritikos.PureMapper.Contracts;

	using Nessos.Expressions.Splicer;

	public class PureMapper : IPureMapperResolver, IPureMapper
	{

		
		private class MapValue
		{
			public LambdaExpression OriginalExpr { get; set; }
			public LambdaExpression SplicedExpr { get; set; }
			public Delegate SplicedFunc { get; set; }

		}

		private readonly Dictionary<(Type Source, Type Dest), MapValue> dict = new Dictionary<(Type, Type), MapValue>();

		public PureMapper(IPureMapperConfig cfg)
			=> Map(cfg?.Maps ?? throw new ArgumentNullException(nameof(cfg)));

		public TDestination Map<TSource, TDestination>(TSource source) where TSource : class
																	   where TDestination : class
		{
			var key = (typeof(TSource), typeof(TDestination));
			if (!dict.ContainsKey(key))
			{
				throw new KeyNotFoundException($"{key}");
			}

			return ((Func<TSource, TDestination>)dict[key].SplicedFunc).Invoke(source);
		}

		private readonly Dictionary<(Type Source, Type Dest), int> visited = new Dictionary<(Type Source, Type Dest), int>();

		public Expression<Func<TSource, TDestination>> ResolveExpr<TSource, TDestination>() where TSource : class where TDestination : class
		{
			this.rec = (Expression<Func<TSource, TDestination>>)(x => null);
			return Resolve<TSource, TDestination>();
	    }

		public Expression<Func<TSource, TDestination>> ResolveFunc<TSource, TDestination>() where TSource : class where TDestination : class
		{
			var key = (typeof(TSource), typeof(TDestination));
			if (!dict.ContainsKey(key))
			{
				throw new KeyNotFoundException($"{key}");
			}

			var mapValue = dict[key];
			this.rec = (Expression<Func<TSource, TDestination>>)(x => ((Func<TSource, TDestination>)mapValue.SplicedFunc)(x));
			return Resolve<TSource, TDestination>();

		}
		private LambdaExpression rec = null;
		public Expression<Func<TSource, TDestination>> Resolve<TSource, TDestination>() where TSource : class
																					    where TDestination : class
		{
			var key = (typeof(TSource), typeof(TDestination));
			if (!dict.ContainsKey(key))
			{
				throw new KeyNotFoundException($"{key}");
			}

			var mapValue = dict[key];
			
			if (visited.ContainsKey(key))
			{
				visited[key] += 1;
				return (Expression<Func<TSource, TDestination>>)this.rec;
			}

			visited.Add(key, 0);

			var splicer = new Splicer();
			var splicedExpr = (LambdaExpression)splicer.Visit(mapValue.OriginalExpr);

			return (Expression<Func<TSource, TDestination>>)splicedExpr;
		}

		private void Map(List<(Type Source, Type Dest, Func<IPureMapperResolver, LambdaExpression> Map)> maps)
		{
			foreach (var (source, destination, map) in maps)
			{
				var key = (source, destination);
				var splicer = new Splicer();

				var mapValue = new MapValue { OriginalExpr = map(this) };

				if (dict.ContainsKey(key))
				{
					dict[key] = mapValue;
				}
				else
				{
					dict.Add(key, mapValue);
				}
			}

			// force resolve
			
			foreach (var keyValue in dict)
			{
				var mapValue = keyValue.Value;

				MethodInfo resolve = typeof(PureMapper).GetMethod("ResolveExpr");
				var _resolve = resolve.MakeGenericMethod(keyValue.Key.Source, keyValue.Key.Dest);
				var lambdaExpression = (LambdaExpression)_resolve.Invoke(this, Array.Empty<object>());
				mapValue.SplicedExpr = lambdaExpression;
				visited.Clear();

				resolve = typeof(PureMapper).GetMethod("ResolveFunc");
				_resolve = resolve.MakeGenericMethod(keyValue.Key.Source, keyValue.Key.Dest);
				lambdaExpression = (LambdaExpression)_resolve.Invoke(this, Array.Empty<object>());
				mapValue.SplicedFunc = lambdaExpression.Compile();

				visited.Clear();
			}
		}
	}
}