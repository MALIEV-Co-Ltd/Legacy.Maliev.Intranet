import copy
import unittest
from po_receipt import valid_receipt


def fixture():
    owner='a'*32
    resources=[dict(owner=owner,state='created',sequence=1),dict(owner=owner,state='verified-absent',sequence=20)]
    cases=[]
    for index,(party,lang) in enumerate([('manual','en'),('shipping','en'),('shipping','th'),('billing','en'),('billing','th')],1):
        names={'shipping':'Synthetic manual shipping','billing':'Synthetic manual billing'}
        if party!='manual':
            names[party]=(f'Synthetic {party.capitalize()} Limited' if lang=='en' else ('บริษัทขนส่งสังเคราะห์' if party=='shipping' else 'บริษัทเรียกเก็บสังเคราะห์'))+' reviewed'
        cases.append(dict(Party=party,Language=lang,Id=index,SupplierId=7,EmployeeId=8,ShippingAddressId=11,BillingAddressId=12,
                          ShippingName=names['shipping'],BillingName=names['billing'],SaveStatus=201,DomainReadStatus=200,BffReadStatus=200,DownloadStatus=200,
                          PdfBytes=1000+index,PdfSha256=f'{index:064x}',ItemId=20+index,LinkId=30+index,SourceGeneration=index*2,DestinationGeneration=index*2+1,
                          ManualFieldsPreserved=True,AddressesPreserved=True,RealDocumentWitness=True,Reloaded=True))
    iam=[]
    for permission in ('legacy-procurement.purchase-orders.create','legacy-procurement.order-items.write','legacy-procurement.files.write'):
        iam.extend(dict(Permission=permission,Resource='global',BypassCache=True,Allowed=True,KnownPrincipal=True) for _ in range(5))
    iam.extend([dict(Permission='legacy-procurement.purchase-orders.create',Resource='global',BypassCache=True,Allowed=False,KnownPrincipal=False),
                dict(Permission='legacy-procurement.files.write',Resource='/purchaseorders/999999',BypassCache=True,Allowed=False,KnownPrincipal=True)])
    row=dict(schema=1,run='123',runAttempt='1',source='b'*40,managedClientCount=51,csrfDenied=400,employeeCreateDenied=403,domainCreateDenied=403,
             companyDenied=403,directCompanyDenied=403,manualLookupDenied=403,foreignTokenDenied=403,foreignResourceDenied=403,
             deniedRequestsPreservedEmptyDatabase=True,cases=cases,iam=iam,scope='finite synthetic only',owner=owner,sequence=10,
             externalFile=dict(syntheticExternalProtocols=True,liveCloud=False,malwareEngine=False,fileSource='d478d2674b57a25c939f62aa838efbedd8591055',
                               storagePatternBlob='6a8a0ab439b9a3a802dee370968b25983a58d4f8',scannerPatternBlob='e5c4fd3e9dc5ec9417edf91d1da7c172032a7ec6',
                               signaturePatternBlob='eedcf5a3a4a1cc964b4077ea99fa43803ab21235',
                               scannedInfected=0,uploads=5,copies=5,deletes=5,scanAttempts=5,scannedLengths=[case['PdfBytes'] for case in cases],scannedSha256=[case['PdfSha256'] for case in cases]),
             provider=[dict(Party=party,Language=lang,Query=f'Synthetic PO {party} {lang}',Status=200) for party in ('shipping','billing') for lang in ('en','th')])
    return row,resources


class ReceiptTests(unittest.TestCase):
    def valid(self,row,resources):
        return valid_receipt(row,resources,'b'*40,'123','1')

    def test_exact_join_accepts_reordered_actual_cases_and_rejects_each_omitted_receipt_member(self):
        row,resources=fixture()
        row['cases'].reverse()
        self.assertTrue(self.valid(row,resources))
        for key in row:
            with self.subTest(key=key):
                changed=copy.deepcopy(row)
                del changed[key]
                self.assertFalse(self.valid(changed,resources))

    def test_foreign_owner_run_attempt_source_cleanup_order_and_fabricated_extra_fields_rejected(self):
        row,resources=fixture()
        for key,value in {'owner':'c'*32,'run':'124','runAttempt':'2','source':'d'*40,'sequence':21,'managedClientCount':50,'csrfDenied':204,'companyDenied':200,'foreignTokenDenied':201,'foreignResourceDenied':404,'extra':True}.items():
            with self.subTest(key=key):
                changed=copy.deepcopy(row)
                changed[key]=value
                self.assertFalse(self.valid(changed,resources))

    def test_both_parties_both_languages_original_distinct_ids_and_ordinary_statuses_required(self):
        row,resources=fixture()
        for key,value in {'Party':'manual','Language':'th','Id':1,'SupplierId':9,'ShippingAddressId':12,'ShippingName':'Synthetic Billing Limited reviewed','SaveStatus':204,'PdfBytes':0,'PdfSha256':'unsafe','DestinationGeneration':2,'Reloaded':False,'IdExtra':True}.items():
            with self.subTest(key=key):
                changed=copy.deepcopy(row)
                changed['cases'][1][key]=value
                self.assertFalse(self.valid(changed,resources))

    def test_real_scanned_pdf_bytes_metadata_promotion_and_explicit_external_limits_required(self):
        row,resources=fixture()
        for key,value in {'liveCloud':True,'malwareEngine':True,'scannedInfected':1,'uploads':4,'copies':4,'deletes':4,'scanAttempts':4,'scannedLengths':[10]*5,'scannedSha256':['a'*64]*5,'fileSource':'a'*40}.items():
            with self.subTest(key=key):
                changed=copy.deepcopy(row)
                changed['externalFile'][key]=value
                self.assertFalse(self.valid(changed,resources))

    def test_cached_or_always_allow_iam_and_missing_physical_company_requests_rejected(self):
        row,resources=fixture()
        for mutation in ('no_bypass','always_allow','no_foreign_resource','unknown_positive','missing_company','wrong_query'):
            changed=copy.deepcopy(row)
            if mutation=='no_bypass': changed['iam'][0]['BypassCache']=False
            elif mutation=='always_allow': changed['iam'][-1]['Allowed']=True
            elif mutation=='no_foreign_resource': changed['iam'].pop()
            elif mutation=='unknown_positive': changed['iam'][0]['KnownPrincipal']=False
            elif mutation=='missing_company': changed['provider'].pop()
            else: changed['provider'][0]['Query']='unbounded'
            with self.subTest(mutation=mutation):
                self.assertFalse(self.valid(changed,resources))

    def test_external_file_exact_emitted_schema_rejects_every_omission_and_fabricated_extra_field(self):
        row,resources=fixture()
        self.assertTrue(self.valid(row,resources))
        for key in row['externalFile']:
            with self.subTest(omitted=key):
                changed=copy.deepcopy(row)
                del changed['externalFile'][key]
                self.assertFalse(self.valid(changed,resources))
        changed=copy.deepcopy(row)
        changed['externalFile']['fabricatedSourceAcceptance']=True
        self.assertFalse(self.valid(changed,resources))

    def test_each_external_pattern_blob_rejects_foreign_wrong_type_or_cross_pattern_source(self):
        row,resources=fixture()
        for key,other in (('storagePatternBlob','scannerPatternBlob'),('scannerPatternBlob','signaturePatternBlob'),('signaturePatternBlob','storagePatternBlob')):
            for value in ('f'*40,None,True,row['externalFile'][other]):
                with self.subTest(key=key,value=value):
                    changed=copy.deepcopy(row)
                    changed['externalFile'][key]=value
                    self.assertFalse(self.valid(changed,resources))


if __name__=='__main__':
    unittest.main()
