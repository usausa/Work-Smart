﻿namespace Smart.Navigation.Attributes
{
    using System;

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class ViewAttribute : Attribute
    {
        public object Id { get; }

        public ViewAttribute(object id)
        {
            Id = id;
        }
    }
}
