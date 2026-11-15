IF EXISTS (SELECT * FROM sys.views WHERE [schema_id] = SCHEMA_ID('trakhound') and [name] = 'package_latest_view')
BEGIN
    DROP VIEW [trakhound].[package_latest_view]
END

GO

CREATE VIEW [trakhound].[package_latest_view] AS

select
[package].[organization],
[package].[uuid],
[package].[id],
[package].[version_major],
[package].[version_minor],
[package].[version_patch],
[package].[category],
[package].[build_date],
[package].[hash]
from [trakhound].[package] as [package]
inner join (select [organization], [id], max([build_date]) as [build_date] from [trakhound].[package] group by [organization], [id]) as [latest] on [package].[organization] = [latest].[organization] and [package].[id] = [latest].[id] and [package].[build_date] = [latest].[build_date];

GO
