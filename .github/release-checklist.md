# Release documentation checklist

Use this checklist when preparing a package release:

- [ ] Compare public XML documentation with the implementation, especially IDs,
      ordering, delivery guarantees, tenant behavior, and failure semantics.
- [ ] Verify the repository and package READMEs compile conceptually against the
      current public registration APIs.
- [ ] Confirm PostgreSQL and SQL Server setup still documents
      `UseEventStore()` and `ExistingDbContext<TDbContext>()` accurately.
- [ ] Document every persistence-model change, including columns, keys, indexes,
      backfills, and provider-specific migration considerations.
- [ ] Verify package-specific limitations stay in the owning provider package
      rather than leaking into provider-neutral Core documentation.
- [ ] Inspect packed artifacts for README and XML documentation files and verify
      dependency metadata with the package-consumer smoke tests.
- [ ] Record intentional public API changes in the compatibility baseline before
      publishing.
