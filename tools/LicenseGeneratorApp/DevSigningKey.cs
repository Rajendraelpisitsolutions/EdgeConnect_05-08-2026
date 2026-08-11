namespace ElpisEdgeConnect.LicenseGeneratorApp;

/// <summary>
/// The RSA private key used to sign generated licenses. This is the DEV key that
/// matches the public key embedded in the current EdgeConnect build, so licenses
/// this tool produces will validate/activate on that build.
///
/// PRODUCTION: replace this with your real private key (the counterpart of the
/// public key compiled into EmbeddedPublicKey.cs). Keep the real key secret —
/// never ship it inside a customer-facing binary.
/// </summary>
internal static class DevSigningKey
{
    public const string PrivatePem =
        "-----BEGIN PRIVATE KEY-----\n" +
        "MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDF5xfrjTdgRtx+\n" +
        "//I/iGqm2qnWYA94IGUdim4DVUhsn/Dd7m8sYDcKCRxwvSKCXVj4Yci+3RyEufHq\n" +
        "cM9h1l1f0jAxwS4pW+Ago7OhhHYAaQY+mYgILRiHrstP/J7KyE48aC1xBJuHVYSg\n" +
        "bnN0ue8ArBSYewbw7fYSlZpCIc7Hj37J5ryJUnGgOzBuOBI54f8zeE1FDnDTRxoj\n" +
        "MhS8wPlgvK4auI7MZGR21sqfQ8H3McTCtbbbCtTrU5eTWLKTnky7WkAe8LCC7flb\n" +
        "th4Jp5SzcKhXp5wUAOt7rW72vFiSCr1yPF3zAKyhUVbYlM3sanPurVEtvqYeaUSG\n" +
        "ncwJmtMBAgMBAAECggEBALeZ8v4scECY3T3RtGwu4ktUOBbT3eYjn8utPu1GtL9a\n" +
        "DzvGVkvEI6vi0gjh3180vTMKfRRdzHRykjZfXHa3Sr94IBA42duzChcd6KwjWNp+\n" +
        "lTWEeMilFcnjZ2oYwzT8amDugaIFlUkUFMxGTETxrwNfomfoU4M4MYACXS+Xb+CA\n" +
        "4+aP6QhA/58+YuDU+XVf2ahQQTeWpBgF8+Oos3cpS7HFcbajoufy4YDoNifY3Rij\n" +
        "GXquP8xoUwg3qC/twfQvtblP6cZDWe+DyL4fYLSTeG6+IqXpjSrM+HFxrFDULA0s\n" +
        "4USuEST24bK+j9kMs9c0HpG809tuH8tXkFBukhF1xtUCgYEA9exubrgI/WWHzhZY\n" +
        "h7986YsOYnB66K3qvN8VAkIpIz+0BHc/h9RmTGG114TVX+TACABEdHd793XqN4+d\n" +
        "qGAqj4bZmNkgff5kdwEZJhHo7jAhP/rej0Sas8XQxEixPusWlYGdqGNAk45xTX4V\n" +
        "f8cAUN4WeJj3qa2WMuKWVeX3WTsCgYEAzgL02Mdc3WBDHbMCWPlrOSsNTUaWcDk5\n" +
        "vOZaRUl7vIiMHpeSkdxeX2iYAz3e2mI9c461rV+0eaj8oCvKBWn3YAxzM1y/QgEc\n" +
        "9VRNGed7sy5pj7aSj+sILRCKx08Q5xiCkqbPKfWgYco21SZRqP/yJwxqPf0qz5Wy\n" +
        "utaFfJ1hYPMCgYBRt4OmlM6f7OnoiDJYwT9vlz5rZXbh9FCI/BPOEU/8H4Hg7gMV\n" +
        "TnXDscAr4j7Iw4kv327fyIhP3UW7uqQnu/TIhoWtrZCHbU3S4XGK3e6pnyKdUO49\n" +
        "aw2A6R2K66DCCHoTqeNKfdiKb42ks13PfL/VH0cWQbYiEsVTGUndNzIu2wKBgA3I\n" +
        "sttCU5tYUoVNMe4EGkGD+OrfuzcdDRjvjMCwDwBpXn65g4wQ45ucovcsj5mrExOF\n" +
        "S/Ciw6+UN/r7kxPTqEKb8qVQIlfnPSnJDzOZgnRVuahs/dd1UWG6hp6ZUrczs6De\n" +
        "WmQjVCzW295dJv+YyHoGYaFuLAwhpwjLS7kvypEzAoGAfIXYGtBTGGM2AW1f3JBb\n" +
        "C9cYCEpbS3e7r1As0oQOOQ5qZQvaBevTP/wfmqb0YziK8eeZTYlm0ReM5AWhpE3v\n" +
        "HI3LyQezogy0sBgLnh5Bzy3AiFq6hFhBNn4iIPr7cDD/ABoKYwLGGatLinlgL79s\n" +
        "1QhMLNKP/AXPKuGdo8ri7CI=\n" +
        "-----END PRIVATE KEY-----\n";
}
