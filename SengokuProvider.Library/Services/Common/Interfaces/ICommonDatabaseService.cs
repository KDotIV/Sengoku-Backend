﻿using Npgsql;

namespace SengokuProvider.Library.Services.Common.Interfaces
{
    public interface ICommonDatabaseService
    {
        public Task<int> CreateAssociativeTable();
        public NpgsqlParameter CreateDBIntArrayType(string parameterName, int[] array);
        public NpgsqlParameter CreateDBTextArrayType(string parameterName, string[] array);
        public NpgsqlParameter CreateDBNumericType(string parameterName, double value);
        public string CleanUrlSlugName(string urlSlug);
        public string[] SanitizeStringArray(string[] input, bool trimQuotes = true);
        public Task<T> MeasureExecutionTimeAsync<T>(Func<Task<T>> func, string operationName);
    }
}