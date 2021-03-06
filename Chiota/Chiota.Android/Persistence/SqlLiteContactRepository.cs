﻿namespace Chiota.Droid.Persistence
{
  using System;
  using System.IO;

  using Chiota.Persistence;

  using SQLite;

  using Tangle.Net.Repository;

  public class SqlLiteContactRepository : AbstractSqlLiteContactRepository
  {
    /// <inheritdoc />
    public SqlLiteContactRepository(IIotaRepository iotaRepository)
      : base(iotaRepository)
    {
    }

    /// <inheritdoc />
    public override SQLiteAsyncConnection Connection
    {
      get
      {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(documentsPath, "ChiotaSQLite.db3");

        return new SQLiteAsyncConnection(path);
      }
    }
  }
}