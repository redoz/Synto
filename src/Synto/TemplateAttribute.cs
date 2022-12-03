using System;

namespace Synto
{
    [Flags]
    public enum TemplateOption
    {
        Default = 0, 
        /// <summary>
        /// Reduces output to only the Body of the templated method.
        /// </summary>
        Bare = 1,

        /// <summary>
        /// Unwraps BlockExpression to first Statement
        /// </summary>
        Single = 2 | Bare

        // should probably add some kind of option to minimize the output
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TemplateAttribute : Attribute
    {
        public TemplateOption Options { get; set; }

        public Type Target { get; }

        public TemplateAttribute(Type target)
        {
            Target = target;
            Options = TemplateOption.Default;
        }

    }

    public delegate void Syntax();

    public delegate T Syntax<T>();

    [AttributeUsage(AttributeTargets.Parameter)]
    public class UnquoteAttribute : Attribute
    {

    }

}
