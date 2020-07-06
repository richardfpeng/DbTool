using System;
using System.Text;
using DbTool.Core;
using DbTool.Core.Entity;

// ReSharper disable once CheckNamespace
namespace DbTool
{
    public class DefaultModelCodeGenerator : IModelCodeGenerator
    {
        private readonly DbProviderFactory _dbProviderFactory;
        private readonly IModelNameConverter _modelNameConverter;

        public DefaultModelCodeGenerator(DbProviderFactory dbProviderFactory, IModelNameConverter modelNameConverter)
        {
            _dbProviderFactory = dbProviderFactory;
            _modelNameConverter = modelNameConverter;
        }

        public string GenerateModelCode(TableEntity tableEntity, ModelCodeGenerateOptions options, string databaseType)
        {
            if (tableEntity == null)
            {
                throw new ArgumentNullException(nameof(tableEntity));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(tableEntity));
            }

            var dbProvider = _dbProviderFactory.GetDbProvider(databaseType);
            var sbText = new StringBuilder();
            sbText.AppendLine("using System;");
            if (options.GenerateDataAnnotation)
            {
                sbText.AppendLine("using System.ComponentModel;");
                sbText.AppendLine("using System.ComponentModel.DataAnnotations;");
                sbText.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");
            }
            sbText.AppendLine();
            sbText.AppendLine($"namespace {options.Namespace}");
            sbText.AppendLine("{");
            if (options.GenerateDataAnnotation && !string.IsNullOrEmpty(tableEntity.TableDescription))
            {
                sbText.AppendLine(
                    $"\t/// <summary>{Environment.NewLine}\t/// {tableEntity.TableDescription.Replace(Environment.NewLine, " ")}{Environment.NewLine}\t/// </summary>");
                sbText.AppendLine($"\t[Table(\"{tableEntity.TableName}\")]");
                sbText.AppendLine($"\t[Description(\"{tableEntity.TableDescription.Replace(Environment.NewLine, " ")}\")]");
            }
            sbText.AppendLine($"\tpublic class {options.Prefix}{_modelNameConverter.ConvertTableToModel(tableEntity.TableName)}{options.Suffix}");
            sbText.AppendLine("\t{");
            var index = 0;
            if (options.GeneratePrivateFields)
            {
                foreach (var item in tableEntity.Columns)
                {
                    if (index > 0)
                    {
                        sbText.AppendLine();
                    }
                    else
                    {
                        index++;
                    }
                    var fclType = dbProvider.DbType2ClrType(item.DataType, item.IsNullable);

                    var tmpColName = ToPrivateFieldName(item.ColumnName);
                    sbText.AppendLine($"\t\tprivate {fclType} {tmpColName};");
                    if (options.GenerateDataAnnotation)
                    {
                        if (!string.IsNullOrEmpty(item.ColumnDescription))
                        {
                            sbText.AppendLine(
                                $"\t\t/// <summary>{Environment.NewLine}\t\t/// {item.ColumnDescription.Replace(Environment.NewLine, " ")}{Environment.NewLine}\t\t/// </summary>");
                            if (options.GenerateDataAnnotation)
                            {
                                sbText.AppendLine($"\t\t[Description(\"{item.ColumnDescription.Replace(Environment.NewLine, " ")}\")]");
                            }
                        }
                        else
                        {
                            if (item.IsPrimaryKey)
                            {
                                sbText.AppendLine($"\t\t[Description(\"主键\")]");
                            }
                        }
                        if (item.IsPrimaryKey)
                        {
                            sbText.AppendLine($"\t\t[Key]");
                        }
                        if (fclType == "string" && item.Size > 0 && item.Size < int.MaxValue)
                        {
                            sbText.AppendLine($"\t\t[StringLength({item.Size})]");
                        }
                        sbText.AppendLine($"\t\t[Column(\"{item.ColumnName}\")]");
                    }
                    sbText.AppendLine($"\t\tpublic {fclType} {item.ColumnName}");
                    sbText.AppendLine("\t\t{");
                    sbText.AppendLine($"\t\t\tget {{ return {tmpColName}; }}");
                    sbText.AppendLine($"\t\t\tset {{ {tmpColName} = value; }}");
                    sbText.AppendLine("\t\t}");
                    sbText.AppendLine();
                }
            }
            else
            {
                foreach (var item in tableEntity.Columns)
                {
                    if (index > 0)
                    {
                        sbText.AppendLine();
                    }
                    else
                    {
                        index++;
                    }
                    var fclType = dbProvider.DbType2ClrType(item.DataType, item.IsNullable);

                    if (options.GenerateDataAnnotation)
                    {
                        if (!string.IsNullOrEmpty(item.ColumnDescription))
                        {
                            sbText.AppendLine(
                                $"\t\t/// <summary>{Environment.NewLine}\t\t/// {item.ColumnDescription.Replace(Environment.NewLine, " ")}{Environment.NewLine}\t\t/// </summary>");
                            if (options.GenerateDataAnnotation)
                            {
                                sbText.AppendLine($"\t\t[Description(\"{item.ColumnDescription.Replace(Environment.NewLine, " ")}\")]");
                            }
                        }
                        if (item.IsPrimaryKey)
                        {
                            sbText.AppendLine($"\t\t[Key]");
                        }
                        if (fclType == "string" && item.Size > 0 && item.Size < int.MaxValue)
                        {
                            sbText.AppendLine($"\t\t[StringLength({item.Size})]");
                        }
                        sbText.AppendLine($"\t\t[Column(\"{item.ColumnName}\")]");
                    }
                    sbText.AppendLine($"\t\tpublic {fclType} {item.ColumnName} {{ get; set; }}");
                }
            }
            sbText.AppendLine("\t}");
            sbText.AppendLine("}");
            return sbText.ToString();
        }

        public string GenerateDialogCode(TableEntity tableEntity, ModelCodeGenerateOptions options, string databaseType)
        {
            if (tableEntity == null)
            {
                throw new ArgumentNullException(nameof(tableEntity));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(tableEntity));
            }

            var dbProvider = _dbProviderFactory.GetDbProvider(databaseType);
            var sbText = new StringBuilder();
            sbText.AppendLine("<el-dialog :title=\"textMap[dialogStatus]\" :visible.sync=\"dialogFormVisible\" width=\"70%\">");
            sbText.AppendLine("<el-form ref=\"dataForm\" :rules = \"rules\" :model = \"temp\" label-position = \"left\" label-width = \"150px\">");
            sbText.AppendLine("<el-row>");
            var index = 0;

            foreach (var item in tableEntity.Columns)
            {
                if (index > 0)
                {
                    sbText.AppendLine();
                }
                else
                {
                    index++;
                }
                var fclType = dbProvider.DbType2ClrType(item.DataType, item.IsNullable);


                sbText.AppendLine("<el-col :span=\"12\">");
                sbText.AppendLine(string.Format("<el-form-item label=\"{0}\" prop=\"{1}\">", CapitalizeFirstLetter(item.ColumnName.Replace("_", " ")), item.ColumnName));
                if (item.DataType == "VARCHAR")
                {
                    sbText.AppendLine(string.Format("<el-input v-model=\"temp.{0}\"  maxlength=\"{1}\"/>", item.ColumnName, item.Size));
                }
                else if (item.DataType == "INT" 
                    || item.DataType == "BIGINT"
                    || item.DataType == "DECIMAL")
                {
                    string precision = "";
                    if (item.DataType == "DECIMAL") precision = ":precision = \"2\"";
                    sbText.AppendLine(string.Format("<el-input-number v-model=\"temp.{0}\"  :step=\"1\" :min=\"0\" {1} />", item.ColumnName, precision));

                }
                else if (item.DataType == "TINYINT")
                {
                    sbText.AppendLine(string.Format("<el-switch v-model=\"temp.{0}\" />", item.ColumnName));
                }
                else if (item.DataType == "DATETIME")
                {
                    sbText.AppendLine(string.Format("<el-date-picker v-model=\"temp.{0}\" format=\"MM/dd/yyyy\" value-format=\"MM/dd/yyyy\" clearable />", item.ColumnName));
                }
                else
                {

                }
                sbText.AppendLine("</el-form-item>");
                sbText.AppendLine("</el-col>");




            }
            sbText.AppendLine("</el-row>");
            sbText.AppendLine("</el-form>");
            sbText.AppendLine("<div slot=\"footer\" class=\"dialog-footer\">");
            sbText.AppendLine("<el-button @click=\"dialogFormVisible = false\">Cancel</el-button>");
            sbText.AppendLine("<el-button type=\"primary\" @click=\"dialogStatus==='create'?createData():updateData()\">Confirm</el-button>");
            sbText.AppendLine("</div>");
            sbText.AppendLine("</el-dialog>");



            return sbText.ToString();
        }

        protected string CapitalizeFirstLetter(string str)
        {
            string ret = "";
            foreach (var item in str.Split(" "))
            {
                if (ret.Length > 0) ret += " ";
                if (item.Length == 1)
                {
                    ret += char.ToUpper(item[0]);
                }else
                {
                    ret += char.ToUpper(item[0]) + item.Substring(1);
                }
            }

            return ret;
        }



        /// <summary>
        /// 将属性名称转换为私有字段名称
        /// </summary>
        /// <param name="propertyName"> 属性名称 </param>
        /// <returns> 私有字段名称 </returns>
        private static string ToPrivateFieldName(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return string.Empty;
            }
            // 全部大写的专有名词
            if (propertyName.Equals(propertyName.ToUpperInvariant()))
            {
                return propertyName.ToLowerInvariant();
            }
            // 首字母大写转成小写
            if (char.IsUpper(propertyName[0]))
            {
                return char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
            }

            return $"_{propertyName}";
        }
    }
}
