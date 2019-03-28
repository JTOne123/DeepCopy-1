using DeepCopyConstructor;

namespace AssemblyToProcess
{
    public static class ClassWithDeepCopyExtension
    {
        [DeepCopyExtension]
        public static SomeObject CopySomeObject(SomeObject source) => source;

        [DeepCopyExtension]
        public static BaseClass CopyBaseClass(BaseClass baseClass) => baseClass;

        [DeepCopyExtension]
        public static BaseClassCollection CopyBaseClassCollection(BaseClassCollection source) => source;

        [DeepCopyExtension]
        public static AbstractBaseClass CopyAbstractBaseClass(AbstractBaseClass source) => source;
    }
}