using System.Text;

namespace FilesMate.Core.Search;

public static class FileNameMatchQuery
{
    public static string ToMatchQuery(string query)
    {
        var builder = new StringBuilder();
        foreach (var part in query.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = part.Replace("\"", string.Empty, StringComparison.Ordinal);
            if (token.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(" AND ");
            }

            builder.Append('"').Append(token).Append('"').Append('*');
        }

        return builder.ToString();
    }
}
