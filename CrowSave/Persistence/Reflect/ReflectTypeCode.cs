namespace CrowSave.Persistence.Reflect
{
    public enum ReflectTypeCode : int
    {
        Int = 1,
        Float = 2,
        Bool = 3,

        StringOptional = 10,
        BytesOptional  = 11,

        Vector3 = 20,
        Quaternion = 21,
        NullableVector3 = 22,
        NullableQuaternion = 23,

        EnumInt = 30,

        IntList = 40,
        FloatList = 41,
        BoolList = 42,
        StringList = 43,
        Vector3List = 44,
    }
}
