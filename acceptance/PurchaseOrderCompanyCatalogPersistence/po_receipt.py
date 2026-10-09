"""Strict business receipt validation; booleans supplement actual IDs/statuses/PDF bytes and causal joins."""
import json
import os
import pathlib
import re

CASE_KEYS = {'Party','Language','Id','SupplierId','EmployeeId','ShippingAddressId','BillingAddressId',
             'ShippingName','BillingName','SaveStatus','DomainReadStatus','BffReadStatus','DownloadStatus',
             'PdfBytes','PdfSha256','ItemId','LinkId','SourceGeneration','DestinationGeneration',
             'ManualFieldsPreserved','AddressesPreserved','RealDocumentWitness','Reloaded'}
TOP_KEYS = {'schema','run','runAttempt','source','managedClientCount','csrfDenied','employeeCreateDenied',
            'domainCreateDenied','companyDenied','directCompanyDenied','manualLookupDenied',
            'deniedRequestsPreservedEmptyDatabase','externalFile','provider','cases','scope','owner','sequence',
            'foreignTokenDenied','foreignResourceDenied','iam'}
EXTERNAL_FILE_KEYS = {'syntheticExternalProtocols','liveCloud','malwareEngine','fileSource',
                      'storagePatternBlob','scannerPatternBlob','signaturePatternBlob',
                      'uploads','copies','deletes','scanAttempts','scannedInfected','scannedLengths','scannedSha256'}
EXTERNAL_PATTERN_BLOBS = {
    'storagePatternBlob':'6a8a0ab439b9a3a802dee370968b25983a58d4f8',
    'scannerPatternBlob':'e5c4fd3e9dc5ec9417edf91d1da7c172032a7ec6',
    'signaturePatternBlob':'eedcf5a3a4a1cc964b4077ea99fa43803ab21235',
}


def valid_receipt(row, resources, source, run, attempt):
    try:
        if (set(row) != TOP_KEYS or row['schema'] != 1 or type(row['schema']) is not int or
            row['run'] != run or row['runAttempt'] != attempt or row['source'] != source or
            not re.fullmatch(r'[0-9a-f]{40}', source) or not re.fullmatch(r'[0-9]+', run) or
            not re.fullmatch(r'[0-9]+', attempt) or not re.fullmatch(r'[0-9a-f]{32}', row['owner']) or
            not resources or any(value['owner'] != row['owner'] for value in resources) or
            type(row['sequence']) is not int or row['sequence'] <= max(value['sequence'] for value in resources if value['state']=='created') or
            row['sequence'] >= min(value['sequence'] for value in resources if value['state']=='verified-absent') or
            row['managedClientCount'] != 51 or type(row['managedClientCount']) is not int or
            row['deniedRequestsPreservedEmptyDatabase'] is not True):
            return False
        for key,status in {'csrfDenied':400,'employeeCreateDenied':403,'domainCreateDenied':403,
                           'companyDenied':403,'directCompanyDenied':403,'manualLookupDenied':403,'foreignTokenDenied':403,'foreignResourceDenied':403}.items():
            if type(row[key]) is not int or row[key] != status:
                return False
        cases = row['cases']
        if not isinstance(cases,list) or len(cases)!=5 or any(set(case)!=CASE_KEYS for case in cases):
            return False
        expected = {('manual','en'),('shipping','en'),('shipping','th'),('billing','en'),('billing','th')}
        if {(case['Party'],case['Language']) for case in cases} != expected:
            return False
        if len({case['Id'] for case in cases})!=5 or len({case['ItemId'] for case in cases})!=5 or len({case['LinkId'] for case in cases})!=5:
            return False
        for key in ('SupplierId','EmployeeId','ShippingAddressId','BillingAddressId'):
            if len({case[key] for case in cases})!=1:
                return False
        for case in cases:
            if any(type(case[key]) is not int or case[key]<=0 for key in ('Id','SupplierId','EmployeeId','ShippingAddressId','BillingAddressId','PdfBytes','ItemId','LinkId','SourceGeneration','DestinationGeneration')):
                return False
            if case['ShippingAddressId']==case['BillingAddressId'] or case['DestinationGeneration']<=case['SourceGeneration'] or case['PdfBytes']<100:
                return False
            if not re.fullmatch(r'[0-9a-f]{64}',case['PdfSha256']):
                return False
            if any(type(case[key]) is not int or case[key]!=status for key,status in {'SaveStatus':201,'DomainReadStatus':200,'BffReadStatus':200,'DownloadStatus':200}.items()):
                return False
            if any(case[key] is not True for key in ('ManualFieldsPreserved','AddressesPreserved','RealDocumentWitness','Reloaded')):
                return False
            for party in ('shipping','billing'):
                name = 'Synthetic manual '+party
                if case['Party']==party:
                    name = ('Synthetic Shipping Limited' if party=='shipping' else 'Synthetic Billing Limited') if case['Language']=='en' else ('บริษัทขนส่งสังเคราะห์' if party=='shipping' else 'บริษัทเรียกเก็บสังเคราะห์')
                    name += ' reviewed'
                if case[party.capitalize()+'Name'] != name:
                    return False
        external=row['externalFile']
        if (not isinstance(external,dict) or set(external)!=EXTERNAL_FILE_KEYS or
            any(external[key]!=blob for key,blob in EXTERNAL_PATTERN_BLOBS.items()) or
            external['syntheticExternalProtocols'] is not True or external['liveCloud'] is not False or external['malwareEngine'] is not False or
            external['fileSource']!='d478d2674b57a25c939f62aa838efbedd8591055' or external['scannedInfected']!=0 or
            external['uploads']!=5 or external['copies']!=5 or external['deletes']!=5 or external['scanAttempts']!=5 or
            sorted(external['scannedLengths'])!=sorted(case['PdfBytes'] for case in cases) or
            sorted(external['scannedSha256'])!=sorted(case['PdfSha256'] for case in cases)):
            return False
        observations=row['provider']
        if not isinstance(observations,list) or not 4<=len(observations)<=8:
            return False
        if {(value['Party'],value['Language']) for value in observations}!={('shipping','en'),('shipping','th'),('billing','en'),('billing','th')}:
            return False
        if any(set(value)!={'Party','Language','Query','Status'} or value['Query']!=f"Synthetic PO {value['Party']} {value['Language']}" or type(value['Status']) is not int or value['Status']!=200 for value in observations):
            return False
        decisions = row['iam']
        if not isinstance(decisions,list) or not decisions or any(set(value)!={'Permission','Resource','BypassCache','Allowed','KnownPrincipal'} or type(value['BypassCache']) is not bool or type(value['Allowed']) is not bool or type(value['KnownPrincipal']) is not bool for value in decisions):
            return False
        for permission in ('legacy-procurement.purchase-orders.create','legacy-procurement.order-items.write','legacy-procurement.files.write'):
            if len([value for value in decisions if value['Permission']==permission and value['Allowed'] and value['KnownPrincipal'] and value['BypassCache']]) < 5:
                return False
        if not any(not value['Allowed'] and not value['KnownPrincipal'] and value['BypassCache'] for value in decisions):
            return False
        if not any(value['Resource']=='/purchaseorders/999999' and not value['Allowed'] and value['KnownPrincipal'] and value['BypassCache'] for value in decisions):
            return False
        return True
    except (KeyError,ValueError,TypeError,AttributeError):
        return False


def journey(directory):
    try:
        lines=(pathlib.Path(directory)/'po-company-journey.jsonl').read_text(encoding='utf-8').splitlines()
        resources=[json.loads(line) for line in (pathlib.Path(directory)/'resources.jsonl').read_text().splitlines()]
        return len(lines)==1 and valid_receipt(json.loads(lines[0]),resources,os.environ.get('CANDIDATE_HEAD',''),os.environ.get('GITHUB_RUN_ID',''),os.environ.get('GITHUB_RUN_ATTEMPT',''))
    except (OSError,ValueError):
        return False
