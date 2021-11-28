using System;
using System.Dynamic;
using System.Linq.Expressions;

namespace Synto
{
    [AttributeUsage(AttributeTargets.Method)]
    public class TemplateAttribute : Attribute
    {
        public Type Target { get; }

        public TemplateAttribute(Type target)
        {
            Target = target;
        }

        public bool Bare { get; set; }
    }


    //public sealed class Syntax 
    //{
    //    DynamicMetaObject IDynamicMetaObjectProvider.GetMetaObject(Expression parameter)
    //    {
    //        throw new NotImplementedException("Something has gone wrong, this object just acts as compile-time placeholder");
    //    }
    //}

    public delegate void Syntax();

    public delegate T Syntax<T>();


    //public sealed class Syntax<T>
    //{
    //    public Syntax()
    //    {
    //    }

    //    public static implicit operator T(Syntax<T> self)
    //    {
    //        // we'll never actually execute this so just need to quiet the compiler here
    //        return default!;
    //    }
    //}
}
