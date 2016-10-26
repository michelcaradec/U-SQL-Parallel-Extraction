# Parallel Extraction from External Data Sources

## Abstract

### Purpose

Build a **custom extractor** to read an external resource (such as Azure Table).

### Problem

Custom extractor behaves like a black box to U-SQL. Extractor is instanciated, and returns rows. Thus, U-SQL is not able to parallelize reads, and dispatch extractor instances to multiple vertices.

How to make U-SQL run extractor **distributed** way (i.e. not processing data source an **atomic** way)?

### Solution Overview

We will use small files stored in Azure Data Lake Store (ADLS) and **File Sets** to instanciate multiple extractors.

``` sql
@data = EXTRACT ... FROM "part-{*}.tsv" USING new CustomExtractor();
```

Each file (`part-{*}.tsv`) will contain instructions used by custom extractor to read data source.

U-SQL will be able to parallelize reads up to the number of input files.

## Implementation Details

Visual Studio solution `ParallelExtraction.sln` contains one U-SQL project `ParallelExtraction.usqlproj`.

U-SQL project `ParallelExtraction.usqproj` contains 3 U-SQL scripts :

- `Setup.usql`
- `PreparePartitions.usql`
- `ReadPartitions.usql`

### Setup.usql

This script will create a test environment:

- A database named `ParallelExtraction`.
- A test file used by the sample provider (see [ReadPartitions.usql](#readpartitions.usql)).

#### About test file

This file is stored as `/parallel/datasource.tsv` in ADLS.

File content:

| partition | value1 | value2 |
|-----------|--------|--------|
| A         | 1      | 26     |
| B         | 2      | 25     |
| C         | 3      | 24     |
| ...       |        |        |
| Y         | 25     | 2      |
| Z         | 26     | 1      |

`partition` column will be used as a partition key.

### PreparePartitions.usql

This script will create files with instructions named `part-*.tsv`.

Files and content:

| filename    | id  | from | to |
|-------------|-----|------|----|
| part-af.tsv | a-f | A    | F  |
| part-gq.tsv | g-q | G    | Q  |
| part-rz.tsv | r-z | R    | Z  |

Each `part-*.tsv`  file contains one line telling which partition range to query. For instance, `part-gq.tsv` file will configure extractor to read from partition G to Q.

### ReadPartitions.usql

This script reads some data source using `PartitionExtractor` custom extractor, and outputs its content to a file named `/parallel/output.tsv`.

To make it simple to test, sample provider reads its data from local U-SQL storage. You must set `@usql_dataroot` value to U-SQL DataRoot (see *Data Lake/Options and Setting...* in Visual Studio) before executing script. By default, storage is located in `%LOCALAPPDATA%\USQLDataRoot`.

#### PartitionExtractor.usql.cs

Data extraction logic is implemented in code-behind file `ReadPartitions.usql.cs`.

| class              | description                              |
| -------------------|------------------------------------------|
| PartitionExtractor | Custom extractor (implements IExtractor) |
| Connection         | Connection informations to datasource    |
| ProviderFactory    | Factory to create requested provider     |
| SampleProvider     | Sample provider (implements IProvider)   |

#### Workflow

![](./media/workflow.png)

1. Rows are requested to custom extractor (**EXTRACT** statement), based on partition instruction file (`part-{partition}.tsv`) read from ADLS.
2. Custom extractor reads `part-{partition}.tsv` file, and retreives reading instructions (partition range).
3. A data provider is instanciated based on connexion string, and configured with partition range information.
4. Provider connects to data source and query data source for requested partition range.
5. Provider returns rows to custom extractor.
6. Custom extractor returns rows to be processed by U-SQL script.
7. Rows will be outputted to file `/parallel/output.tsv`.

Content of output file `output.tsv`:

| extractor_id | partition_id | partition | value1 | value2 |
|--------------|--------------|-----------|--------|--------|
| 699778080553 | a-f          | A         | 1      | 26     |
| 699778080553 | a-f          | B         | 2      | 25     |
| ...          |              |           |        |        |
| 699778080553 | a-f          | F         | 6      | 21     |
| 699777776621 | g-q          | G         | 7      | 20     |
| 699777776621 | g-q          | H         | 8      | 19     |
| ...          |              |           |        |        |
| 699777776621 | g-q          | Q         | 17     | 10     |
| 699778060500 | r-z          | R         | 18     | 9      |
| 699778060500 | r-z          | S         | 19     | 8      |
| ...          |              |           |        |        |
| 699778060500 | r-z          | Z         | 26     | 1      |

`extractor_id`  values (which purpose is to witness one extractor instance per partition range was used) will change after each script execution, but will remain distinct between partition ranges (`partition_id` column).

