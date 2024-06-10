using System;
using System.Linq;
using System.Reflection;

namespace RitualWorks
{
    public static class GenericMapper
    {
        public static TTarget MapTo<TSource, TTarget>(TSource source)
            where TTarget : new()
        {
            var target = new TTarget();
            CopyProperties(source, target);
            return target;
        }

        public static void UpdateFrom<TSource, TTarget>(TSource source, TTarget target)
        {
            CopyProperties(source, target);
        }

        private static void CopyProperties<TSource, TTarget>(TSource source, TTarget target)
        {
            var sourceProperties = typeof(TSource).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var targetProperties = typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var sourceProperty in sourceProperties)
            {
                var targetProperty = targetProperties.FirstOrDefault(tp => tp.Name == sourceProperty.Name && tp.PropertyType == sourceProperty.PropertyType);
                if (targetProperty != null && targetProperty.CanWrite)
                {
                    targetProperty.SetValue(target, sourceProperty.GetValue(source));
                }
            }
        }
    }
}
	

