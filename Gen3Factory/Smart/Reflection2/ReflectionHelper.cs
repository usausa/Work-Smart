namespace Smart.Reflection2
{
    using System;

    public static class ReflectionHelper
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Ignore")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1806:DoNotIgnoreMethodResults", Justification = "Ignore")]
        static ReflectionHelper()
        {
            try
            {
                var type = Type.GetType("System.Reflection.Emit.DynamicMethod");
                if (type != null)
                {
                    Activator.CreateInstance(type, string.Empty, typeof(object), Type.EmptyTypes, true);
                    IsCodegenAllowed = true;
                }
            }
            catch
            {
                // Ignore
            }
        }

        public static bool IsCodegenAllowed { get; }
    }
}