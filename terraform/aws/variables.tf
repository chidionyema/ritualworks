variable "aws_region" {
  description = "AWS region where the resources will be created"
  type        = string
}

variable "aws_eks_name" {
  description = "Name of the EKS cluster"
  type        = string
}

variable "domain_name" {
  description = "The domain name for the ACM certificate"
  type        = string
}

variable "aws_s3_bucket_name" {
  description = "The name of the S3 bucket"
  type        = string
}
